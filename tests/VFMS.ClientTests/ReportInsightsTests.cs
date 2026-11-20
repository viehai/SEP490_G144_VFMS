using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.SystemAdministration;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>Business report comparison text, chart bars, CSV export and the trend request.</summary>
public sealed class ReportInsightsTests
{
    private static ReportMetricResponse Metric(string type, decimal value, decimal? previous, string valueType = "Count") =>
        new(type, type, value, valueType, previous);

    [Fact]
    public void RevenueRiseIsGoodNewsWithAPercentage()
    {
        var change = ReportInsights.Compare(Metric("TotalRevenue", 5_600_000m, 5_000_000m, "Currency"))!;

        Assert.Equal(ReportInsights.Trend.Up, change.Direction);
        Assert.Equal(12m, change.Percent);
        Assert.True(change.IsGood);
        Assert.Equal("↑ 12% so với kỳ trước", change.Text);
    }

    [Fact]
    public void MoreEquipmentIssuesIsBadNews()
    {
        var change = ReportInsights.Compare(Metric("EquipmentIssues", 6, 4))!;

        Assert.False(change.IsGood);
        Assert.Equal("↑ 50% so với kỳ trước", change.Text);
        Assert.True(ReportInsights.Compare(Metric("EquipmentIssues", 2, 4))!.IsGood);
    }

    [Fact]
    public void FewerNewMembersIsADrop()
    {
        var change = ReportInsights.Compare(Metric("NewMembers", 9, 12))!;

        Assert.Equal(ReportInsights.Trend.Down, change.Direction);
        Assert.Equal(-25m, change.Percent);
        Assert.False(change.IsGood);
        Assert.Equal("↓ 25% so với kỳ trước", change.Text);
    }

    [Fact]
    public void LockerOccupancyChangesInPercentagePoints()
    {
        var change = ReportInsights.Compare(Metric("LockerOccupancy", 42.5m, 37.5m, "Percentage"))!;

        Assert.Null(change.Percent);
        Assert.Equal("↑ 5 điểm % so với kỳ trước", change.Text);
    }

    [Fact]
    public void FromZeroThereIsNoPercentage()
    {
        var change = ReportInsights.Compare(Metric("SupplementSales", 1_250_000m, 0, "Currency"))!;

        Assert.Null(change.Percent);
        Assert.Equal("↑ 1.250.000 ₫ (kỳ trước bằng 0)", change.Text);
    }

    [Fact]
    public void SameValueAndMissingPreviousValue()
    {
        Assert.Equal("Không đổi so với kỳ trước", ReportInsights.Compare(Metric("NewMembers", 3, 3))!.Text);
        Assert.Null(ReportInsights.Compare(Metric("NewMembers", 3, null)));
    }

    [Theory]
    [InlineData(1250000.5, "Currency", "1.250.000,5 ₫")]
    [InlineData(37.5, "Percentage", "37,5%")]
    [InlineData(1234, "Count", "1.234")]
    public void ValuesUseVietnameseNumberFormat(decimal value, string type, string expected) =>
        Assert.Equal(expected, ReportInsights.Format(value, type));

    [Fact]
    public void BarsAreScaledToTheLargestValue()
    {
        Assert.Equal([50d, 100d, 2d, 0d], ReportInsights.BarHeights([5m, 10m, 0.01m, 0m]));
        Assert.Equal([0d, 0d], ReportInsights.BarHeights([0m, 0m]));
    }

    [Fact]
    public void PeriodLabelsAreShortForTheChart()
    {
        Assert.Equal("10/2026", ReportInsights.PeriodLabel("Month", new DateOnly(2026, 10, 1)));
        Assert.Equal("26/10", ReportInsights.PeriodLabel("Week", new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void CsvHasTheReportAndOneRowPerMetric()
    {
        var report = new BusinessReportResponse(1, "Monthly report 2026-10", "Month", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            "m", "Manager", new DateTime(2026, 11, 2, 3, 0, 0, DateTimeKind.Utc), true,
            [Metric("TotalRevenue", 5_600_000m, 5_000_000m, "Currency"), Metric("NewMembers", 3, null)],
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), BusinessReportResponse.FromSnapshot);

        var bytes = ReportInsights.ToCsv(report, m => m.MetricType == "TotalRevenue" ? "Tổng doanh thu, VND" : "Hội viên mới");
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("Báo cáo,Monthly report 2026-10", lines[0]);
        Assert.Equal("Kỳ trước,01/09/2026 - 30/09/2026,Từ báo cáo đã lưu", lines[2]);
        Assert.Contains("Chỉ số,Đơn vị,Giá trị,Kỳ trước,Chênh lệch,Thay đổi (%)", lines);
        Assert.Contains("\"Tổng doanh thu, VND\",VND,5600000,5000000,600000,12", lines);
        Assert.Contains("Hội viên mới,Số lượng,3,,,", lines);
        Assert.Equal("VFMS-bao-cao-2026-10-01-2026-10-31.csv", ReportInsights.CsvFileName(report));
    }

    [Fact]
    public void CsvStopsFormulasInText()
    {
        var report = new BusinessReportResponse(1, "=HYPERLINK(\"x\")", "Day", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1),
            "m", null, DateTime.UtcNow, false, []);

        var first = Encoding.UTF8.GetString(ReportInsights.ToCsv(report, m => m.MetricName)[3..]).Split("\r\n")[0];

        Assert.Equal("Báo cáo,\"'=HYPERLINK(\"\"x\"\")\"", first);
    }

    [Fact]
    public async Task TrendRequestSendsPeriodDateAndCount()
    {
        Uri? sent = null;
        var http = new HttpClient(new Handler(request =>
        {
            sent = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new BusinessReportTrendResponse("Month", [])) };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "manager", "Manager", "manager@example.test", ["Manager"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        using var api = new SystemAdministrationApiClient(http, new AuthSession(new Storage(stored), new AuthApiClient(http)));

        await api.GetReportTrendAsync(new BusinessReportTrendRequest("Month", new DateOnly(2026, 10, 1), 6));

        Assert.Equal("/api/business-reports/trend", sent!.AbsolutePath);
        Assert.Equal("?periodType=Month&date=2026-10-01&count=6", sent.Query);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class Storage(string value) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((T)(object)value) : throw new InvalidOperationException(identifier);
    }
}
