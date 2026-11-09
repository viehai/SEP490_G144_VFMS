using System.Text.Json.Serialization;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.SystemAdministration;

// ── General Policies (UC-84) ─────────────────────────────────────

/// <summary>
/// Gym settings (UC-84). Times are gym local time ("HH:mm" or "HH:mm:ss").
/// Opening and closing time are set together; opening must be earlier than closing.
/// Omitted renewal windows leave the current policy unchanged.
/// </summary>
public sealed record GymSettingsRequest(
    string GymName,
    string? Address,
    string? PhoneNumber,
    string? Email,
    [property: JsonConverter(typeof(FlexibleTimeOnlyConverter))] TimeOnly? OpenTime,
    [property: JsonConverter(typeof(FlexibleTimeOnlyConverter))] TimeOnly? CloseTime,
    bool IsAcceptingMembers,
    int? RenewalAdvanceDays = null,
    int? RenewalGraceDays = null
);

public sealed record GymSettingsResponse(
    int Id,
    string GymName,
    string? Address,
    string? PhoneNumber,
    string? Email,
    TimeOnly? OpenTime,
    TimeOnly? CloseTime,
    bool IsAcceptingMembers,
    DateTime UpdatedAt,
    int RenewalAdvanceDays,
    int RenewalGraceDays
);

// ── Business Reports (UC-86) ─────────────────────────────────────

/// <summary>
/// Generate a report (UC-86). PeriodType: Day, Week (Monday to Sunday) or Month; <see cref="Date"/> is any
/// gym date inside the period. The period cannot end in the future. When a report for the same period exists,
/// the request is refused unless <see cref="ConfirmNew"/> is true.
/// </summary>
public sealed record GenerateBusinessReportRequest(
    string PeriodType,
    DateOnly Date,
    bool ConfirmNew = false
);

/// <summary>Reports whose period overlaps From..To (gym dates, inclusive).</summary>
public sealed record BusinessReportListRequest(
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record ReportMetricResponse(
    string MetricType,
    string MetricName,
    decimal Value,
    string ValueType
);

public sealed record BusinessReportSummaryResponse(
    int Id,
    string ReportName,
    string PeriodType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string GeneratedByUserId,
    string? GeneratedByName,
    DateTime GeneratedAt
);

public sealed record BusinessReportResponse(
    int Id,
    string ReportName,
    string PeriodType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string GeneratedByUserId,
    string? GeneratedByName,
    DateTime GeneratedAt,
    bool HasData,
    IReadOnlyCollection<ReportMetricResponse> Metrics
);
