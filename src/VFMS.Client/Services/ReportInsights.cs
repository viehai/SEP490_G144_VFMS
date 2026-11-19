using System.Globalization;
using System.Text;
using VFMS.Contracts.SystemAdministration;

namespace VFMS.Client.Services;

/// <summary>Change against the previous period, number formats and CSV export for business reports (UC-86).</summary>
public static class ReportInsights
{
    /// <summary>Direction of a change, and whether it is good news for the gym.</summary>
    public enum Trend { None, Up, Down, Same }

    /// <summary>
    /// A metric's change against the previous period. <see cref="Percent"/> is null when the previous value was 0
    /// (no percentage can be given). Locker occupancy is already a percentage, so it changes by percentage points.
    /// </summary>
    public sealed record Change(Trend Direction, decimal Difference, decimal? Percent, bool IsGood, string Text);

    private static readonly NumberFormatInfo Vietnamese = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    /// <summary>Metrics where a rise is bad news.</summary>
    private static readonly HashSet<string> LowerIsBetter = ["EquipmentIssues"];

    public static Change? Compare(ReportMetricResponse metric)
    {
        if (metric.PreviousValue is not { } previous) return null;
        var difference = metric.Value - previous;
        if (difference == 0) return new(Trend.Same, 0, 0, true, "Không đổi so với kỳ trước");

        var direction = difference > 0 ? Trend.Up : Trend.Down;
        var arrow = direction == Trend.Up ? "↑" : "↓";
        var isGood = (direction == Trend.Up) != LowerIsBetter.Contains(metric.MetricType);
        if (metric.ValueType == "Percentage")
            return new(direction, difference, null, isGood, $"{arrow} {Number(Math.Abs(difference), "0.##")} điểm % so với kỳ trước");
        if (previous == 0)
            return new(direction, difference, null, isGood, $"{arrow} {Format(metric with { Value = Math.Abs(difference) })} (kỳ trước bằng 0)");

        var percent = Math.Round(difference * 100m / Math.Abs(previous), 1);
        return new(direction, difference, percent, isGood, $"{arrow} {Number(Math.Abs(percent), "0.#")}% so với kỳ trước");
    }

    /// <summary>A metric value as shown on screen: "1.250.000 ₫", "37,5%" or "12".</summary>
    public static string Format(ReportMetricResponse metric) => Format(metric.Value, metric.ValueType);

    public static string Format(decimal value, string valueType) => valueType switch
    {
        "Currency" => $"{Number(value, "#,##0.##")} ₫",
        "Percentage" => $"{Number(value, "0.##")}%",
        _ => Number(value, "#,##0"),
    };

    /// <summary>Short axis label for a trend period: "05/11" for a day or week, "11/2026" for a month.</summary>
    public static string PeriodLabel(string periodType, DateOnly start) =>
        periodType == "Month" ? start.ToString("MM/yyyy", CultureInfo.InvariantCulture) : start.ToString("dd/MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Bar heights for a trend chart as percentages of the largest value. A value above 0 gets at least 2% so its bar
    /// stays visible; 0 gets no bar.
    /// </summary>
    public static IReadOnlyList<double> BarHeights(IReadOnlyList<decimal> values)
    {
        var max = values.Count == 0 ? 0m : values.Max();
        return values.Select(v => max <= 0 || v <= 0 ? 0d : Math.Max(2d, (double)(v / max * 100m))).ToArray();
    }

    /// <summary>
    /// The report as CSV for Excel: UTF-8 with a byte order mark so Vietnamese text shows correctly, one row per
    /// metric with the previous period and the change. Numbers are plain (no grouping) so Excel can calculate with them.
    /// </summary>
    public static byte[] ToCsv(BusinessReportResponse report, Func<ReportMetricResponse, string> label)
    {
        var csv = new StringBuilder();
        Row(csv, "Báo cáo", report.ReportName);
        Row(csv, "Kỳ", $"{report.PeriodStart:dd/MM/yyyy} - {report.PeriodEnd:dd/MM/yyyy}");
        if (report.PreviousPeriodStart is { } prevStart && report.PreviousPeriodEnd is { } prevEnd)
            Row(csv, "Kỳ trước", $"{prevStart:dd/MM/yyyy} - {prevEnd:dd/MM/yyyy}",
                report.PreviousSource == BusinessReportResponse.FromSnapshot ? "Từ báo cáo đã lưu" : "Tính từ dữ liệu hiện tại");
        Row(csv, "Tạo lúc", report.GeneratedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        csv.Append("\r\n");
        Row(csv, "Chỉ số", "Đơn vị", "Giá trị", "Kỳ trước", "Chênh lệch", "Thay đổi (%)");
        foreach (var metric in report.Metrics)
        {
            var change = Compare(metric);
            Row(csv, label(metric), Unit(metric.ValueType), Plain(metric.Value),
                metric.PreviousValue is { } previous ? Plain(previous) : "",
                change is null ? "" : Plain(change.Difference),
                change?.Percent is { } percent ? Plain(percent) : "");
        }
        var bom = Encoding.UTF8.GetPreamble();
        return [.. bom, .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>A safe file name such as "VFMS-bao-cao-2026-10-01-2026-10-31.csv".</summary>
    public static string CsvFileName(BusinessReportResponse report) =>
        $"VFMS-bao-cao-{report.PeriodStart:yyyy-MM-dd}-{report.PeriodEnd:yyyy-MM-dd}.csv";

    private static string Unit(string valueType) => valueType switch { "Currency" => "VND", "Percentage" => "%", _ => "Số lượng" };

    private static string Number(decimal value, string format) => value.ToString(format, Vietnamese);

    private static string Plain(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Row(StringBuilder csv, params string[] cells) => CsvFile.AppendRow(csv, cells);
}
