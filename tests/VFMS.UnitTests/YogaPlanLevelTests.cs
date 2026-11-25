using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using Xunit;

namespace VFMS.UnitTests;

/// <summary>Yoga plans carry the highest class level they book. Buyers keep the level they bought.</summary>
public sealed class YogaPlanLevelTests
{
    [Theory]
    [InlineData("YOGA", "Intermediate", true)]
    [InlineData("YOGA", "intermediate", true)]
    [InlineData("YOGA", "Expert", false)]
    [InlineData("GYM", null, true)]
    public void ValidatorAcceptsOnlyKnownLevels(string serviceType, string? level, bool valid) =>
        Assert.Equal(valid, new MembershipPlanRequestValidator().Validate(Request(serviceType, level)).IsValid);

    [Fact]
    public async Task NewYogaPlanNeedsALevel()
    {
        var service = new MembershipPlanService(new Fake(), TimeProvider.System);

        var missing = await service.CreateAsync(Request("YOGA", null), "manager", null, default);
        var created = await service.CreateAsync(Request("YOGA", "advanced"), "manager", null, default);

        Assert.Equal("YOGA_LEVEL_REQUIRED", missing.ErrorCode);
        Assert.Equal("Advanced", created.Data!.YogaDifficultyLevel);   // stored as the canonical code
    }

    [Theory]
    [InlineData(false, null, "Intermediate", null, "Intermediate")]              // unused old plan: the manager gives it a level
    [InlineData(false, null, null, "YOGA_LEVEL_REQUIRED", null)]
    [InlineData(true, null, "Intermediate", "YOGA_LEVEL_LOCKED", null)]          // bought old plan keeps "all levels" for its buyers
    [InlineData(true, null, null, null, null)]                                     // ...and can still be renamed or repriced
    [InlineData(true, "Beginner", "Advanced", "YOGA_LEVEL_LOCKED", "Beginner")]
    [InlineData(true, "Beginner", "beginner", null, "Beginner")]
    public async Task BoughtPlanKeepsItsLevel(bool bought, string? current, string? requested, string? error, string? stored)
    {
        var repo = new Fake { Used = bought };
        repo.Plan.Components.Single().YogaDifficultyLevel = current;
        var service = new MembershipPlanService(repo, TimeProvider.System);

        var result = await service.UpdateAsync(1, Request("YOGA", requested), "manager", null, default);

        Assert.Equal(error, result.ErrorCode);
        Assert.Equal(stored, repo.Plan.Components.Single().YogaDifficultyLevel);
    }

    private static MembershipPlanRequest Request(string serviceType, string? level) => new()
    {
        Name = "Yoga", DurationInDays = 30, Price = 600_000m, Status = "ACTIVE", ServiceType = serviceType, YogaDifficultyLevel = level
    };

    private sealed class Fake : IMembershipPlanRepository
    {
        public bool Used { get; set; }
        public Package Plan { get; } = new()
        {
            Id = 1, Name = "Yoga", Price = 600_000m, PackageStatus = PackageStatus.Active,
            Components = [new PackageComponent { Id = 1, ComponentType = ComponentType.YOGA, DurationDays = 30 }]
        };

        public Task<Package?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<Package?>(Plan);
        public Task<bool> HasUsageAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Used);
        public Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity { }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MembershipPlanReadModel?> GetDetailsAsync(int id, PackageStatus? requiredStatus, CancellationToken cancellationToken = default,
            DateTime? sellableAtUtc = null) =>
            Task.FromResult<MembershipPlanReadModel?>(new(1, Plan.Name, null, 30, null, Plan.Price, Plan.PackageStatus, 0,
                ComponentType.YOGA, Plan.Components.Single().YogaDifficultyLevel));
        public Task<(IReadOnlyList<MembershipPlanReadModel> Items, int TotalCount)> SearchAsync(string? search, PackageStatus? status, int page,
            int pageSize, CancellationToken cancellationToken = default, ComponentType? serviceType = null, DateTime? sellableAtUtc = null,
            VFMS.Contracts.Memberships.MembershipDurationUnit? durationUnit = null, MembershipFreezeAllowanceFilter? freezeAllowanceFilter = null) =>
            throw new NotSupportedException();
        public void Remove(Package package) => throw new NotSupportedException();
        public Task<CatalogMembershipFreezeRules?> GetCatalogFreezeRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CatalogMembershipFreezeRules?>(null);
        public Task<(IReadOnlyList<PackageCatalogReadModel> Items, int TotalCount)> SearchByComponentTypeAsync(ComponentType componentType,
            int page, int pageSize, DateTime nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PackageCatalogReadModel>> ListActiveSingleServicePackagesAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
