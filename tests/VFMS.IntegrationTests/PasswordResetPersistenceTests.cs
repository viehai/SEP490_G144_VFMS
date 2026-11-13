using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class PasswordResetPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;
    private readonly PasswordHasher _hasher = new();
    private readonly EmailFake _email = new();
    private const string UserId = "reset-user";

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Reset test gym" };
        db.Gyms.Add(gym);
        db.ApplicationUsers.Add(new() { UserId = UserId, Gym = gym, Email = "member@example.com", FullName = "Test Member", PasswordHash = _hasher.Hash("OldPass@123"), AccountStatus = AccountStatus.Active });
        db.RefreshTokens.Add(new() { UserId = UserId, Token = "old-session", ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task EmailTokenIsHashedAtRestAndCanOnlyBeUsedOnce()
    {
        await using var db = CreateContext();
        var service = Service(db);
        await service.ProcessEmailAsync("MEMBER@EXAMPLE.COM");
        var rawToken = RawToken();
        var saved = await db.ApplicationUsers.AsNoTracking().SingleAsync();
        Assert.NotEqual(rawToken, saved.ResetTokenHash);
        Assert.Equal(Hash(rawToken), saved.ResetTokenHash);
        Assert.InRange(saved.ResetTokenExpiresAt!.Value, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));

        var first = await service.ResetAsync(new(rawToken, "NewPass@123", "NewPass@123"));
        var replay = await service.ResetAsync(new(rawToken, "NextPass@123", "NextPass@123"));
        Assert.True(first.IsSuccess);
        Assert.False(replay.IsSuccess);
        var user = await db.ApplicationUsers.AsNoTracking().SingleAsync();
        Assert.True(_hasher.Verify("NewPass@123", user.PasswordHash));
        Assert.False(_hasher.Verify("OldPass@123", user.PasswordHash));
        Assert.All(await db.RefreshTokens.AsNoTracking().ToListAsync(), token => Assert.NotNull(token.RevokedAt));
        Assert.Null(user.ResetTokenHash);
        Assert.Null(user.ResetTokenExpiresAt);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("password-changed")]
    [InlineData("locked")]
    [InlineData("banned")]
    [InlineData("deleted")]
    public async Task InvalidatedCredentialsCannotResetPassword(string reason)
    {
        await using var db = CreateContext();
        var service = Service(db);
        await service.ProcessEmailAsync("member@example.com");
        db.ChangeTracker.Clear();
        var user = await db.ApplicationUsers.SingleAsync();
        if (reason == "expired") user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        if (reason == "consumed") { user.ResetTokenHash = null; user.ResetTokenExpiresAt = null; }
        if (reason == "password-changed") user.PasswordHash = _hasher.Hash("Different@123");
        if (reason == "locked") user.AccountStatus = AccountStatus.Locked;
        if (reason == "banned") user.IsBanned = true;
        if (reason == "deleted") user.IsDeleted = true;
        await db.SaveChangesAsync();
        var result = await service.ResetAsync(new(RawToken(), "NewPass@123", "NewPass@123"));
        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH_INVALID_RESET_TOKEN", result.ErrorCode);
    }

    [Fact]
    public async Task OnlyOneResetWinsAgainstSameTokenAndPasswordSnapshot()
    {
        string expectedHash;
        var tokenHash = Hash("same-token");
        await using (var db = CreateContext())
        {
            var user = await db.ApplicationUsers.SingleAsync();
            expectedHash = user.PasswordHash;
            user.ResetTokenHash = tokenHash;
            user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync();
        }
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var first = await new PasswordResetRepository(firstContext).TryResetAsync(UserId, tokenHash, expectedHash, _hasher.Hash("Winner@123"), DateTime.UtcNow, default);
        var second = await new PasswordResetRepository(secondContext).TryResetAsync(UserId, tokenHash, expectedHash, _hasher.Hash("Loser@123"), DateTime.UtcNow, default);
        Assert.True(first);
        Assert.False(second);
        Assert.Null((await secondContext.ApplicationUsers.AsNoTracking().SingleAsync()).ResetTokenHash);
        Assert.True(_hasher.Verify("Winner@123", (await secondContext.ApplicationUsers.AsNoTracking().SingleAsync()).PasswordHash));
    }

    [Fact]
    public async Task FailureRevokingSessionsRollsBackPasswordAndTokenConsumption()
    {
        await using var db = CreateContext();
        var user = await db.ApplicationUsers.SingleAsync();
        var originalHash = user.PasswordHash;
        var tokenHash = Hash("ROLLBACK");
        user.ResetTokenHash = tokenHash;
        user.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(15);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_revocation BEFORE UPDATE ON RefreshTokens BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => new PasswordResetRepository(db).TryResetAsync(
            UserId, tokenHash, originalHash, _hasher.Hash("NewPass@123"), DateTime.UtcNow, default));
        Assert.Equal(originalHash, (await db.ApplicationUsers.AsNoTracking().SingleAsync()).PasswordHash);
        Assert.Equal(tokenHash, (await db.ApplicationUsers.AsNoTracking().SingleAsync()).ResetTokenHash);
    }

    [Fact]
    public async Task ResendAfterCooldownReplacesLinkAndOlderFailureCannotClearIt()
    {
        await using var db = CreateContext();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var service = Service(db, clock);
        await service.ProcessEmailAsync("member@example.com");
        var first = RawToken();
        await service.ProcessEmailAsync("member@example.com");
        Assert.Equal(1, _email.Count);

        clock.Now = clock.Now.AddSeconds(61);
        await service.ProcessEmailAsync("member@example.com");
        var second = RawToken();
        Assert.Equal(2, _email.Count);
        Assert.NotEqual(first, second);
        Assert.False((await service.ResetAsync(new(first, "NewPass@123", "NewPass@123"))).IsSuccess);

        await new PasswordResetRepository(db).InvalidateAsync(UserId, Hash(first), clock.Now.UtcDateTime, default);
        Assert.Equal(Hash(second), (await db.ApplicationUsers.AsNoTracking().SingleAsync()).ResetTokenHash);
        Assert.True((await service.ResetAsync(new(second, "NewPass@123", "NewPass@123"))).IsSuccess);
    }

    [Fact]
    public async Task OrdinaryPasswordChangeClearsEvenAResetTokenIssuedAfterUserWasLoaded()
    {
        await using var editContext = CreateContext();
        var user = await editContext.ApplicationUsers.SingleAsync();
        Assert.Null(user.ResetTokenHash);
        await using (var issueContext = CreateContext())
            await Service(issueContext).ProcessEmailAsync("member@example.com");

        user.PasswordHash = _hasher.Hash("Changed@123");
        await editContext.SaveChangesAsync();
        var saved = await editContext.ApplicationUsers.AsNoTracking().SingleAsync();
        Assert.Null(saved.ResetTokenHash);
        Assert.Null(saved.ResetTokenExpiresAt);
    }

    [Fact]
    public async Task ProfileEditsDoNotInvalidateResetLink()
    {
        await using var db = CreateContext();
        await Service(db).ProcessEmailAsync("member@example.com");
        var user = await db.ApplicationUsers.SingleAsync();
        user.FullName = "Updated profile";
        await db.SaveChangesAsync();
        Assert.Equal(Hash(RawToken()), (await db.ApplicationUsers.AsNoTracking().SingleAsync()).ResetTokenHash);
    }

    private SqliteTestContext CreateContext() => new(_options);
    private PasswordResetService Service(VfmsDbContext db, TimeProvider? clock = null) => new(new PasswordResetRepository(db), _hasher, _email, new QueueFake(),
        new() { ResetUrl = "https://fitness.example/auth/reset-password" }, clock ?? TimeProvider.System);
    private string RawToken() => Regex.Match(_email.Body!, @"#token=([A-F0-9]{64})").Groups[1].Value;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class QueueFake : IPasswordResetEmailQueue { public bool TryEnqueue(string email) => true; }
    private sealed class EmailFake : IEmailService
    {
        public bool IsConfigured => true;
        public string? Body;
        public int Count;
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default) { Body = body; Count++; return Task.CompletedTask; }
    }
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
