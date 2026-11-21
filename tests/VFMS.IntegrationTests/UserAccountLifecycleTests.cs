using Mapster;
using MapsterMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class UserAccountLifecycleTests : IAsyncLifetime
{
    private const string ActorId = "admin";
    private const string CustomerId = "customer";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Lifecycle test gym" };
        db.ApplicationUsers.AddRange(
            new ApplicationUser
            {
                UserId = ActorId,
                Gym = gym,
                FullName = "Test Administrator",
                Email = "admin@example.test",
                PhoneNumber = "0900000001",
                AccountStatus = AccountStatus.Active
            },
            new ApplicationUser
            {
                UserId = CustomerId,
                Gym = gym,
                FullName = "Test Customer",
                Email = "customer@example.test",
                PhoneNumber = "0900000002"
            });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [InlineData(AccountStatus.PendingVerification)]
    [InlineData(AccountStatus.Active)]
    [InlineData(AccountStatus.Inactive)]
    public async Task LockAndUnlockPreservePriorStatus(AccountStatus originalStatus)
    {
        await using var db = CreateContext();
        var user = await db.ApplicationUsers.SingleAsync(user => user.UserId == CustomerId);
        user.AccountStatus = originalStatus;
        if (originalStatus == AccountStatus.Active)
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.UserId,
                Token = "synthetic-test-refresh-session",
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
        }
        await db.SaveChangesAsync();

        var locked = await Service(db).LockUserAsync(CustomerId, ActorId, "127.0.0.1");

        Assert.True(locked.IsSuccess);
        Assert.Equal(nameof(AccountStatus.Locked), locked.Data!.AccountStatus);
        var savedLock = await db.ApplicationUsers.AsNoTracking().SingleAsync(user => user.UserId == CustomerId);
        Assert.Equal(AccountStatus.Locked, savedLock.AccountStatus);
        Assert.Equal(originalStatus, savedLock.AccountStatusBeforeLock);
        if (originalStatus == AccountStatus.Active)
            Assert.NotNull(await db.RefreshTokens.Select(token => token.RevokedAt).SingleAsync());

        // Unlock from a new request context to prove the pre-lock state was persisted.
        await using var unlockDb = CreateContext();
        var unlocked = await Service(unlockDb).UnlockUserAsync(CustomerId, ActorId, "127.0.0.1");

        Assert.True(unlocked.IsSuccess);
        Assert.Equal(originalStatus.ToString(), unlocked.Data!.AccountStatus);
        var savedUnlock = await unlockDb.ApplicationUsers.AsNoTracking().SingleAsync(user => user.UserId == CustomerId);
        Assert.Equal(originalStatus, savedUnlock.AccountStatus);
        Assert.Null(savedUnlock.AccountStatusBeforeLock);
        var audit = await unlockDb.AuditLogs.OrderBy(log => log.Id).ToListAsync();
        Assert.Collection(audit,
            lockedLog =>
            {
                Assert.Equal(AuditActionType.AccountLock, lockedLog.ActionType);
                Assert.Equal(originalStatus.ToString(), lockedLog.OldValue);
                Assert.Equal(nameof(AccountStatus.Locked), lockedLog.NewValue);
            },
            unlockedLog =>
            {
                Assert.Equal(AuditActionType.AccountUnlock, unlockedLog.ActionType);
                Assert.Equal(nameof(AccountStatus.Locked), unlockedLog.OldValue);
                Assert.Equal(originalStatus.ToString(), unlockedLog.NewValue);
            });
    }

    [Theory]
    [InlineData(AccountStatus.PendingVerification, false, AccountStatus.PendingVerification)]
    [InlineData(AccountStatus.Active, false, AccountStatus.Active)]
    [InlineData(null, true, AccountStatus.PendingVerification)]
    [InlineData(null, false, AccountStatus.Inactive)]
    public async Task LegacyUnlockDoesNotAssumeAccountWasActive(AccountStatus? loggedStatus, bool hasSetupExpiry, AccountStatus expected)
    {
        await using var db = CreateContext();
        var user = await db.ApplicationUsers.SingleAsync(user => user.UserId == CustomerId);
        user.AccountStatus = AccountStatus.Locked;
        if (hasSetupExpiry)
            user.PasswordSetupExpiresAt = DateTime.UtcNow.AddHours(48);
        if (loggedStatus.HasValue)
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = ActorId,
                AuditEntity = $"ApplicationUser:{CustomerId}",
                ActionType = AuditActionType.AccountLock,
                OldValue = loggedStatus.Value.ToString(),
                NewValue = nameof(AccountStatus.Locked),
                OccurredAt = DateTime.UtcNow.AddDays(-1)
            });
        }
        await db.SaveChangesAsync();

        var result = await Service(db).UnlockUserAsync(CustomerId, ActorId, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.ToString(), result.Data!.AccountStatus);
        Assert.Equal(expected, await db.ApplicationUsers.Where(user => user.UserId == CustomerId)
            .Select(user => user.AccountStatus).SingleAsync());
    }

    [Fact]
    public async Task StaleLockCannotOverwriteCompletedOnboarding()
    {
        await using var lockDb = CreateContext();
        var tracked = await lockDb.ApplicationUsers.SingleAsync(user => user.UserId == CustomerId);
        Assert.Equal(AccountStatus.PendingVerification, tracked.AccountStatus);
        await using var setupDb = CreateContext();
        await setupDb.ApplicationUsers.Where(user => user.UserId == CustomerId)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.AccountStatus, AccountStatus.Active));

        var result = await Service(lockDb).LockUserAsync(CustomerId, ActorId, null);

        Assert.Equal("USER_ACCOUNT_STATUS_CHANGED", result.ErrorCode);
        Assert.Equal(AccountStatus.Active, await setupDb.ApplicationUsers.Where(user => user.UserId == CustomerId)
            .Select(user => user.AccountStatus).SingleAsync());
        Assert.Empty(await setupDb.AuditLogs.ToListAsync());
    }

    private VfmsDbContext CreateContext() => new SqliteTestContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options);

    private static UserAdministrationService Service(VfmsDbContext db)
    {
        var mapping = new TypeAdapterConfig();
        new UserAdministrationMapping().Register(mapping);
        return new UserAdministrationService(new UserAdministrationRepository(db), new PasswordHasher(), new Mapper(mapping));
    }
}
