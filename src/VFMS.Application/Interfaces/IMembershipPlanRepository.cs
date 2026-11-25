using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Contracts.Memberships;
using MembershipDurationUnit = VFMS.Contracts.Memberships.MembershipDurationUnit;

namespace VFMS.Application.Interfaces;

public sealed record MembershipPlanReadModel(
    int Id,
    string Name,
    string? Description,
    int? DurationDays,
    int? DurationMonths,
    decimal Price,
    PackageStatus Status,
    int SubscriberCount,
    ComponentType ServiceType = ComponentType.GYM,
    string? YogaDifficultyLevel = null)
{
    public bool IsSingleService { get; init; } = true;
    public string? TargetAudience { get; init; }
    public int? MaxFreezeCount { get; init; }
    public int? MinFreezeDaysPerRequest { get; init; }
    public int? MaxFreezeDaysPerRequest { get; init; }
}

/// <summary>
/// Read model cho public package catalog — phản ánh đúng các field trong PackageComponent.
/// </summary>
public sealed record PackageCatalogReadModel(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    PackageStatus Status,
    ComponentType ComponentType,
    int? DurationDays,
    int? DurationMonths,
    int? SessionCount,
    string? YogaDifficultyLevel,
    string? ApplicableTrainerLevel,
    int? PTFreezeMinimumDays,
    int? PTFreezeMaxTotalDays,
    bool AllowFreeze,
    int? MaxFreezeDaysPerRequest,
    int? MaxFreezeCount,
    TimeOnly? AccessStartTime,
    TimeOnly? AccessEndTime)
{
    public bool IsSingleService { get; init; } = true;
    public string? TargetAudience { get; init; }
    public int? MinFreezeDaysPerRequest { get; init; }
}

public sealed record CatalogMembershipFreezeRules(int? MinimumDays, int MaximumDays, int DefaultMaxFreezeCount = 0);

public interface IMembershipPlanRepository : IAuditedRepository
{
    Task<(IReadOnlyList<MembershipPlanReadModel> Items, int TotalCount)> SearchAsync(
        string? search, PackageStatus? status, int page, int pageSize, CancellationToken cancellationToken = default,
        ComponentType? serviceType = null, DateTime? sellableAtUtc = null,
        MembershipDurationUnit? durationUnit = null, MembershipFreezeAllowanceFilter? freezeAllowanceFilter = null);

    /// <param name="sellableAtUtc">Set for the public catalog: hides yoga-only plans while no yoga class can be booked.</param>
    Task<MembershipPlanReadModel?> GetDetailsAsync(
        int id, PackageStatus? requiredStatus, CancellationToken cancellationToken = default, DateTime? sellableAtUtc = null);

    Task<Package?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> HasUsageAsync(int id, CancellationToken cancellationToken = default);
    void Remove(Package package);
    Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trả về các Package Active có ít nhất một PackageComponent không bị xóa mềm
    /// thuộc <paramref name="componentType"/>.
    /// </summary>
    Task<(IReadOnlyList<PackageCatalogReadModel> Items, int TotalCount)> SearchByComponentTypeAsync(
        ComponentType componentType, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageCatalogReadModel>> ListActiveSingleServicePackagesAsync(CancellationToken cancellationToken);
    Task<CatalogMembershipFreezeRules?> GetCatalogFreezeRulesAsync(CancellationToken cancellationToken);
}
