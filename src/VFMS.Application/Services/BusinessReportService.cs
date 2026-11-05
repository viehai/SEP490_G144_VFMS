using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-86 View Business Reports. A report covers one day, week (Monday to Sunday) or month of gym dates (UTC+7)
/// and is saved as a read-only snapshot with one metric of each type (GBR-63). Source data is only read.
/// </summary>
public sealed class BusinessReportService : IBusinessReportService
{
    private static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    private readonly IBusinessReportRepository _repository;

    public BusinessReportService(IBusinessReportRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PagedResponse<BusinessReportSummaryResponse>>> GetReportsAsync(
        BusinessReportListRequest request, CancellationToken cancellationToken = default)
    {
        if (request.From is { } f && request.To is { } t && f > t)
            return Result<PagedResponse<BusinessReportSummaryResponse>>.Failure(
                "The start date cannot be after the end date.", "BUSINESS_REPORT_INVALID_DATE_RANGE");

        var (items, total) = await _repository.SearchAsync(request.From, request.To, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<BusinessReportSummaryResponse>>.Success(new PagedResponse<BusinessReportSummaryResponse>(
            request.Page, request.PageSize, total, items.Select(ToSummary).ToArray()));
    }

    public async Task<Result<BusinessReportResponse>> GetReportAsync(int id, CancellationToken cancellationToken = default)
    {
        var report = await _repository.GetAsync(id, cancellationToken);
        return report is null ? NotFound() : Result<BusinessReportResponse>.Success(ToResponse(report));
    }

    public async Task<Result<BusinessReportResponse>> GenerateAsync(
        GenerateBusinessReportRequest request, string userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!TryGetPeriod(request.PeriodType, request.Date, out var start, out var end, out var name))
            return Result<BusinessReportResponse>.Failure("Period type must be Day, Week or Month.", "BUSINESS_REPORT_INVALID_PERIOD");

        var now = DateTime.UtcNow;
        var gymToday = DateOnly.FromDateTime(now + GymOffset);
        if (end > gymToday)
            return Result<BusinessReportResponse>.Failure(
                $"The selected period ends on {end:yyyy-MM-dd}, which is in the future. Choose a period that has ended.",
                "BUSINESS_REPORT_PERIOD_IN_FUTURE");

        // UC-86 AF3: the same period was already reported; only generate another snapshot when asked to.
        if (!request.ConfirmNew && await _repository.FindByPeriodAsync(start, end, cancellationToken) is { } existing)
            return Result<BusinessReportResponse>.Failure(
                $"A report for this period already exists (ID {existing.Id}, generated {existing.GeneratedAt:yyyy-MM-dd HH:mm} UTC). " +
                "Open it, or send confirmNew = true to generate a new snapshot.",
                "BUSINESS_REPORT_EXISTS", 409);

        var source = await _repository.GetSourceDataAsync(
            start, end, ToUtc(start), ToUtc(end.AddDays(1)), cancellationToken);

        var report = new BusinessReportSnapshot
        {
            ReportName = name,
            PeriodStart = start,
            PeriodEnd = end,
            GeneratedByUserId = userId,
            GeneratedAt = now,
            Metrics = BuildMetrics(source).ToList()
        };
        var audit = AuditedWriter.Audit(userId, AuditActionType.Create, nameof(BusinessReportSnapshot), ipAddress,
            newValue: $"Name={name}; Period={start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
        await AuditedWriter.SaveNewAsync(_repository, report, audit, null, cancellationToken);

        var saved = await _repository.GetAsync(report.Id, cancellationToken) ?? report;
        return Result<BusinessReportResponse>.Success(ToResponse(saved), 201);
    }

    // ── Helpers ──────────────────────────────────────────────────

    internal static IEnumerable<ReportMetric> BuildMetrics(ReportSourceData s)
    {
        ReportMetric M(ReportMetricType type, string name, decimal value, MetricValueType valueType) =>
            new() { MetricType = type, MetricName = name, MetricValue = value, ValueType = valueType };

        var occupancy = s.TotalLockers == 0 ? 0m : Math.Round(s.OccupiedLockers * 100m / s.TotalLockers, 2);
        return
        [
            M(ReportMetricType.TotalRevenue, "Total Revenue", s.MembershipRevenue + s.SupplementSales, MetricValueType.Currency),
            M(ReportMetricType.NewMembers, "New Members", s.NewMembers, MetricValueType.Count),
            M(ReportMetricType.ActiveMemberships, "Active Memberships", s.ActiveMemberships, MetricValueType.Count),
            M(ReportMetricType.PTSessionsCompleted, "PT Sessions Completed", s.PtSessionsCompleted, MetricValueType.Count),
            M(ReportMetricType.YogaClassAttendance, "Yoga Class Attendance", s.YogaAttendance, MetricValueType.Count),
            M(ReportMetricType.LockerOccupancy, "Locker Occupancy", occupancy, MetricValueType.Percentage),
            M(ReportMetricType.SupplementSales, "Supplement Sales", s.SupplementSales, MetricValueType.Currency),
            M(ReportMetricType.EquipmentIssues, "Equipment Issues", s.EquipmentIssues, MetricValueType.Count)
        ];
    }

    internal static bool TryGetPeriod(string? periodType, DateOnly date, out DateOnly start, out DateOnly end, out string name)
    {
        switch (periodType?.Trim().ToLowerInvariant())
        {
            case "day":
                start = end = date;
                name = $"Daily report {date:yyyy-MM-dd}";
                return true;
            case "week":
                start = date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday
                end = start.AddDays(6);
                name = $"Weekly report {start:yyyy-MM-dd} to {end:yyyy-MM-dd}";
                return true;
            case "month":
                start = new DateOnly(date.Year, date.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                name = $"Monthly report {start:yyyy-MM}";
                return true;
            default:
                start = end = default;
                name = string.Empty;
                return false;
        }
    }

    internal static string PeriodTypeOf(DateOnly start, DateOnly end) =>
        start == end ? "Day"
        : start.DayOfWeek == DayOfWeek.Monday && end == start.AddDays(6) ? "Week"
        : start.Day == 1 && end == start.AddMonths(1).AddDays(-1) ? "Month"
        : "Custom";

    private static DateTime ToUtc(DateOnly gymDate) =>
        DateTime.SpecifyKind(gymDate.ToDateTime(TimeOnly.MinValue) - GymOffset, DateTimeKind.Utc);

    private static BusinessReportSummaryResponse ToSummary(BusinessReportSnapshot r) =>
        new(r.Id, r.ReportName, PeriodTypeOf(r.PeriodStart, r.PeriodEnd), r.PeriodStart, r.PeriodEnd,
            r.GeneratedByUserId, r.GeneratedByUser?.FullName, r.GeneratedAt);

    private static BusinessReportResponse ToResponse(BusinessReportSnapshot r)
    {
        var metrics = r.Metrics
            .OrderBy(m => m.MetricType)
            .Select(m => new ReportMetricResponse(m.MetricType.ToString(), m.MetricName, m.MetricValue, m.ValueType.ToString()))
            .ToArray();
        return new BusinessReportResponse(r.Id, r.ReportName, PeriodTypeOf(r.PeriodStart, r.PeriodEnd), r.PeriodStart, r.PeriodEnd,
            r.GeneratedByUserId, r.GeneratedByUser?.FullName, r.GeneratedAt,
            HasData: metrics.Any(m => m.Value != 0), metrics);
    }

    private static Result<BusinessReportResponse> NotFound() =>
        Result<BusinessReportResponse>.Failure("Business report was not found.", "BUSINESS_REPORT_NOT_FOUND", 404);
}
