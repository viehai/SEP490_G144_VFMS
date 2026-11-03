using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Read-only snapshot of business metrics for a given period.
/// Manager views historical business metrics (GBR-63). Never modified after generation.
/// </summary>
public class BusinessReportSnapshot : BaseEntity
{
    public string ReportName { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string GeneratedByUserId { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser GeneratedByUser { get; set; } = null!;
    public ICollection<ReportMetric> Metrics { get; set; } = [];
}

