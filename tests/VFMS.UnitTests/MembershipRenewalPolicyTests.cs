using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;

namespace VFMS.UnitTests;

public sealed class MembershipRenewalPolicyTests
{
    private static readonly GymSettingsRequest ExistingClientRequest = new(
        "VFMS", null, null, null, null, null, true);

    [Fact]
    public async Task ManagerCanSetZeroAndAnOmittedWindowRetainsItsConfiguredValue()
    {
        var repository = new GymSettingsStub();
        var service = new GymSettingsService(repository);

        var updated = await service.UpdateAsync(
            ExistingClientRequest with { RenewalAdvanceDays = 0 }, "manager", null);
        var unchanged = await service.UpdateAsync(ExistingClientRequest, "manager", null);

        Assert.True(updated.IsSuccess);
        Assert.True(unchanged.IsSuccess);
        Assert.Equal(0, unchanged.Data!.RenewalAdvanceDays);
        Assert.Equal(17, unchanged.Data.RenewalGraceDays);
        var audit = Assert.Single(repository.Audits);
        Assert.Contains("RenewalAdvanceDays=5", audit.OldValue);
        Assert.Contains("RenewalAdvanceDays=0", audit.NewValue);
        Assert.Equal("manager", audit.ActorUserId);
    }

    [Theory]
    [InlineData(-1, 7)]
    [InlineData(3, -1)]
    public async Task InvalidWindowsAreRejectedBeforeSaving(int advance, int grace)
    {
        var repository = new GymSettingsStub();
        var service = new GymSettingsService(repository);

        var result = await service.UpdateAsync(
            ExistingClientRequest with { RenewalAdvanceDays = advance, RenewalGraceDays = grace }, "manager", null);

        Assert.False(result.IsSuccess);
        Assert.Equal("GYM_SETTINGS_INVALID_RENEWAL_WINDOW", result.ErrorCode);
        Assert.Equal(5, repository.Gym.RenewalAdvanceDays);
        Assert.Equal(17, repository.Gym.RenewalGraceDays);
        Assert.Empty(repository.Audits);
    }

    private sealed class GymSettingsStub : IGymSettingsRepository
    {
        public Gym Gym { get; } = new() { GymName = "VFMS", RenewalAdvanceDays = 5, RenewalGraceDays = 17 };
        public List<AuditLog> Audits { get; } = [];

        public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) => Task.FromResult<Gym?>(Gym);
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => Audits.Add(Assert.IsType<AuditLog>(entity));
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
