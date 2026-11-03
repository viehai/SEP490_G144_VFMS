using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single metric value within a <see cref="BusinessReportSnapshot"/>.
/// </summary>
public class ReportMetric : BaseEntity
{
    public int BusinessReportSnapshotId { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public ReportMetricType MetricType { get; set; }
    public decimal MetricValue { get; set; }
    public MetricValueType ValueType { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public BusinessReportSnapshot BusinessReportSnapshot { get; set; } = null!;
}
