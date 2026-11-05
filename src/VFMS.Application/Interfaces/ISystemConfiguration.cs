using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IGymSettingsRepository : IAuditedRepository
{
    /// <summary>The gym the system runs (the first one; the system manages a single gym).</summary>
    Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default);
}

/// <summary>UC-84 Manage General Policies.</summary>
public interface IGymSettingsService
{
    Task<Result<GymSettingsResponse>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result<GymSettingsResponse>> UpdateAsync(
        GymSettingsRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raw figures for one report period, read from the source tables without changing them (GBR-63).
/// Date-based facts use the gym dates; timestamp-based facts use the matching UTC range.
/// </summary>
public sealed record ReportSourceData(
    decimal MembershipRevenue,
    decimal SupplementSales,
    int NewMembers,
    int ActiveMemberships,
    int PtSessionsCompleted,
    int YogaAttendance,
    int TotalLockers,
    int OccupiedLockers,
    int EquipmentIssues);

public interface IBusinessReportRepository : IAuditedRepository
{
    Task<(IReadOnlyList<BusinessReportSnapshot> Items, int TotalCount)> SearchAsync(
        DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<BusinessReportSnapshot?> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>The newest report with exactly this period, if any.</summary>
    Task<BusinessReportSnapshot?> FindByPeriodAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);
    Task<ReportSourceData> GetSourceDataAsync(
        DateOnly start, DateOnly end, DateTime startUtc, DateTime endUtcExclusive, CancellationToken cancellationToken = default);
}

/// <summary>UC-86 View Business Reports. GBR-63: read-only snapshots over past periods.</summary>
public interface IBusinessReportService
{
    Task<Result<PagedResponse<BusinessReportSummaryResponse>>> GetReportsAsync(
        BusinessReportListRequest request, CancellationToken cancellationToken = default);
    Task<Result<BusinessReportResponse>> GetReportAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<BusinessReportResponse>> GenerateAsync(
        GenerateBusinessReportRequest request, string userId, string? ipAddress, CancellationToken cancellationToken = default);
}
