using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Application.Settings;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class PasswordResetTests
{
    [Fact]
    public async Task RequestNeverLooksUpAccountAndReturnsSameAcceptedResponse()
    {
        var repository = new RepositoryFake();
        var queue = new QueueFake();
        var service = Create(repository, queue, new EmailFake());
        var first = await service.RequestAsync(new("registered@example.com"));
        var second = await service.RequestAsync(new("unknown@example.com"));
        Assert.Equal(202, first.StatusCode);
        Assert.Equal(first.Data, second.Data);
        Assert.Equal(0, repository.Lookups);
        Assert.Equal(2, queue.Emails.Count);
    }

    [Fact]
    public async Task UnconfiguredEmailAndFullQueueReturnUnavailableForAllAddresses()
    {
        var repository = new RepositoryFake();
        var queue = new QueueFake();
        var email = new EmailFake { IsConfigured = false };
        var service = Create(repository, queue, email);
        Assert.Equal(503, (await service.RequestAsync(new("one@example.com"))).StatusCode);
        Assert.Empty(queue.Emails);
        email.IsConfigured = true;
        queue.Accept = false;
        Assert.Equal(503, (await service.RequestAsync(new("two@example.com"))).StatusCode);
        Assert.Equal(0, repository.Lookups);
    }

    [Fact]
    public async Task WorkerSkipsUnknownAndRecentlyRequestedAccounts()
    {
        var repository = new RepositoryFake();
        var email = new EmailFake();
        var service = Create(repository, new QueueFake(), email);
        await service.ProcessEmailAsync("unknown@example.com");
        repository.User = new() { UserId = "user", Email = "known@example.com", PasswordHash = "hash", AccountStatus = AccountStatus.Active };
        repository.Recent = true;
        await service.ProcessEmailAsync("known@example.com");
        Assert.Equal(0, repository.Issued);
        Assert.Null(email.Body);
    }

    [Fact]
    public async Task DeliveryFailureInvalidatesGeneratedTokenAndDoesNotReturnItToCaller()
    {
        var repository = new RepositoryFake { User = new() { UserId = "user", Email = "user@example.com", PasswordHash = "hash" } };
        var email = new EmailFake { Fail = true };
        var service = Create(repository, new QueueFake(), email);
        await Assert.ThrowsAsync<IOException>(() => service.ProcessEmailAsync("user@example.com"));
        Assert.Equal(1, repository.Issued);
        Assert.True(repository.Invalidated);
        Assert.Null(repository.User!.ResetTokenHash);
    }

    [Theory]
    [InlineData("weak", "weak")]
    [InlineData("NewPass@123", "Different@123")]
    [InlineData("NoNumber!", "NoNumber!")]
    public void ResetRejectsWeakOrUnconfirmedPasswords(string password, string confirmation)
    {
        Assert.False(new ResetPasswordRequestValidator().Validate(new ResetPasswordRequest(new string('A', 64), password, confirmation)).IsValid);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://untrusted.example/reset")]
    [InlineData("https://trusted.example/reset#token=old")]
    [InlineData("https://trusted.example/reset?returnUrl=elsewhere")]
    public void ResetSettingsRequireTrustedAbsoluteUrlWithoutQueryOrFragment(string url) =>
        Assert.False(new PasswordResetSettings { ResetUrl = url }.IsValid);

    private static PasswordResetService Create(RepositoryFake repository, QueueFake queue, EmailFake email) =>
        new(repository, new HasherFake(), email, queue, new() { ResetUrl = "https://fitness.example/auth/reset-password" }, TimeProvider.System);

    private sealed class HasherFake : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string password, string hash) => hash == Hash(password);
    }
    private sealed class QueueFake : IPasswordResetEmailQueue
    {
        public bool Accept = true;
        public List<string> Emails = [];
        public bool TryEnqueue(string email) { if (!Accept) return false; Emails.Add(email); return true; }
    }
    private sealed class EmailFake : IEmailService
    {
        public bool IsConfigured { get; set; } = true;
        public bool Fail;
        public string? Body;
        public Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default)
        {
            Body = textBody;
            return Fail ? Task.FromException(new IOException("SMTP unavailable")) : Task.CompletedTask;
        }
    }
    private sealed class RepositoryFake : IPasswordResetRepository
    {
        public ApplicationUser? User;
        public bool Recent;
        public int Lookups;
        public int Issued;
        public bool Invalidated;
        public Task<ApplicationUser?> FindEligibleUserAsync(string email, CancellationToken ct) { Lookups++; return Task.FromResult(User); }
        public Task<bool> TryIssueAsync(string userId, string oldHash, string tokenHash, DateTime expiresAt, DateTime cutoff, DateTime now, CancellationToken ct)
        {
            if (Recent || User is null) return Task.FromResult(false);
            Issued++;
            User.ResetTokenHash = tokenHash;
            User.ResetTokenExpiresAt = expiresAt;
            return Task.FromResult(true);
        }
        public Task<ApplicationUser?> FindByTokenAsync(string hash, CancellationToken ct) => Task.FromResult(User?.ResetTokenHash == hash ? User : null);
        public Task InvalidateAsync(string userId, string tokenHash, DateTime now, CancellationToken ct)
        {
            Invalidated = true;
            User!.ResetTokenHash = null;
            User.ResetTokenExpiresAt = null;
            return Task.CompletedTask;
        }
        public Task<bool> TryResetAsync(string userId, string tokenHash, string oldHash, string newHash, DateTime now, CancellationToken ct) => throw new NotImplementedException();
    }
}
