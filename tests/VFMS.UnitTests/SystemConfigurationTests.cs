using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.SystemAdministration;
using VFMS.Contracts.SystemAdministration.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;

namespace VFMS.UnitTests;

public sealed class SystemConfigurationTests
{
    private const string Manager = "manager-1";

    private static GymSettingsRequest Settings(TimeOnly? open = null, TimeOnly? close = null) =>
        new("Victor  Fitness", "1 Le Loi", "0243 1234 567", "Contact@VFMS.vn",
            open ?? new TimeOnly(5, 0), close ?? new TimeOnly(23, 0), true);

    private static DateOnly GymToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    // ── General Policies (UC-84) ─────────────────────────────────

    [Fact]
    public void SettingsValidatorChecksNameContactsAndHours()
    {
        var v = new GymSettingsRequestValidator();
        Assert.True(v.Validate(Settings()).IsValid);
        Assert.True(v.Validate(Settings() with { PhoneNumber = null, Email = " ", Address = null }).IsValid);
        Assert.False(v.Validate(Settings() with { GymName = " " }).IsValid);
        Assert.False(v.Validate(Settings() with { Email = "not-an-email" }).IsValid);
        Assert.False(v.Validate(Settings() with { PhoneNumber = "call me" }).IsValid);
        Assert.False(v.Validate(Settings(new TimeOnly(22, 0), new TimeOnly(6, 0))).IsValid);
        Assert.False(v.Validate(Settings(new TimeOnly(8, 0), new TimeOnly(8, 0))).IsValid);
        Assert.False(v.Validate(Settings() with { CloseTime = null }).IsValid);
        Assert.True(v.Validate(Settings() with { OpenTime = null, CloseTime = null }).IsValid);
    }

    [Fact]
    public async Task UpdateSavesCleanValuesAndAuditsOldAndNew()
    {
        var repo = new GymRepo();
        var result = await new GymSettingsService(repo).UpdateAsync(Settings() with { IsAcceptingMembers = false }, Manager, "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal("Victor Fitness", result.Data!.GymName);
        Assert.Equal("contact@vfms.vn", result.Data.Email);
        Assert.False(result.Data.IsAcceptingMembers);
        var audit = Assert.Single(repo.Audits);
        Assert.Contains("Name=Old Gym", audit.OldValue);
        Assert.Contains("AcceptingMembers=False", audit.NewValue);
    }

    [Fact]
    public async Task UnchangedSettingsAreNotAudited()
    {
        var repo = new GymRepo();
        var service = new GymSettingsService(repo);
        await service.UpdateAsync(Settings(), Manager, null);
        await service.UpdateAsync(Settings(), Manager, null);
        Assert.Single(repo.Audits);
    }

    [Fact]
    public async Task ServiceRejectsClosingBeforeOpening()
    {
        var result = await new GymSettingsService(new GymRepo())
            .UpdateAsync(Settings(new TimeOnly(23, 0), new TimeOnly(5, 0)), Manager, null);
        Assert.Equal("GYM_SETTINGS_INVALID_HOURS", result.ErrorCode);
    }

    [Fact]
    public void TimesAcceptShortAndLongForm()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        GymSettingsRequest Parse(string open, string close) => JsonSerializer.Deserialize<GymSettingsRequest>(
            $"{{\"gymName\":\"G\",\"openTime\":{open},\"closeTime\":{close},\"isAcceptingMembers\":true}}", options)!;

        Assert.Equal(new TimeOnly(5, 30), Parse("\"05:30\"", "\"23:00:00\"").OpenTime);
        Assert.Null(Parse("null", "null").CloseTime);
        Assert.Throws<JsonException>(() => Parse("\"25:99\"", "null"));
        Assert.Contains("\"openTime\":\"05:30:00\"", JsonSerializer.Serialize(Parse("\"05:30\"", "null"), options));
    }

    // ── Business Reports (UC-86) ─────────────────────────────────

    [Theory]
    [InlineData("Day", "2026-11-04", "2026-11-04", "2026-11-04", "Báo cáo ngày 04/11/2026")]
    [InlineData("week", "2026-10-28", "2026-10-26", "2026-11-01", "Báo cáo tuần 26/10 – 01/11/2026")]
    [InlineData("Week", "2026-11-01", "2026-10-26", "2026-11-01", "Báo cáo tuần 26/10 – 01/11/2026")]
    [InlineData("Month", "2026-02-14", "2026-02-01", "2026-02-28", "Báo cáo tháng 02/2026")]
    public async Task PeriodIsExpandedFromTheChosenDate(string type, string date, string start, string end, string name)
    {
        var repo = new ReportRepo();
        var result = await new BusinessReportService(repo).GenerateAsync(new(type, DateOnly.Parse(date)), Manager, null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(DateOnly.Parse(start), result.Data!.PeriodStart);
        Assert.Equal(DateOnly.Parse(end), result.Data.PeriodEnd);
        Assert.Equal(name, result.Data.ReportName);
        Assert.Equal(char.ToUpper(type[0]) + type[1..].ToLower(), result.Data.PeriodType);
        // The UTC range starts at gym midnight (UTC+7).
        Assert.Equal(DateOnly.Parse(start).ToDateTime(TimeOnly.MinValue).AddHours(-7), repo.LastStartUtc);
        Assert.Equal(DateOnly.Parse(end).AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7), repo.LastEndUtc);
    }

    [Fact]
    public async Task PeriodEndingInTheFutureIsRejected()
    {
        var service = new BusinessReportService(new ReportRepo());
        Assert.Equal("BUSINESS_REPORT_PERIOD_IN_FUTURE",
            (await service.GenerateAsync(new("Day", GymToday.AddDays(1)), Manager, null)).ErrorCode);
        Assert.Equal("BUSINESS_REPORT_PERIOD_IN_FUTURE",
            (await service.GenerateAsync(new("Month", GymToday.AddMonths(1)), Manager, null)).ErrorCode);
        Assert.True((await service.GenerateAsync(new("Day", GymToday), Manager, null)).IsSuccess);
    }

    [Fact]
    public async Task MetricsAreBuiltFromSourceData()
    {
        var repo = new ReportRepo
        {
            Source = new ReportSourceData(5_000_000m, 1_250_000m, 3, 40, 12, 25, 8, 3, 2)
        };
        var report = (await new BusinessReportService(repo).GenerateAsync(new("Day", new DateOnly(2026, 11, 1)), Manager, null)).Data!;

        var m = report.Metrics.ToDictionary(x => x.MetricType);
        Assert.Equal(8, m.Count);
        Assert.Equal(6_250_000m, m["TotalRevenue"].Value);
        Assert.Equal("Currency", m["TotalRevenue"].ValueType);
        Assert.Equal(1_250_000m, m["SupplementSales"].Value);
        Assert.Equal(3, m["NewMembers"].Value);
        Assert.Equal(40, m["ActiveMemberships"].Value);
        Assert.Equal(12, m["PTSessionsCompleted"].Value);
        Assert.Equal(25, m["YogaClassAttendance"].Value);
        Assert.Equal(37.5m, m["LockerOccupancy"].Value);
        Assert.Equal("Percentage", m["LockerOccupancy"].ValueType);
        Assert.Equal(2, m["EquipmentIssues"].Value);
        Assert.True(report.HasData);
    }

    [Fact]
    public async Task EmptyPeriodIsSavedWithZeros()
    {
        var repo = new ReportRepo { Source = new ReportSourceData(0, 0, 0, 0, 0, 0, 0, 0, 0) };
        var report = (await new BusinessReportService(repo).GenerateAsync(new("Day", new DateOnly(2026, 1, 1)), Manager, null)).Data!;

        Assert.False(report.HasData);
        Assert.All(report.Metrics, x => Assert.Equal(0, x.Value));
        Assert.Single(repo.Reports);
    }

    [Fact]
    public async Task SamePeriodNeedsConfirmationForANewSnapshot()
    {
        var repo = new ReportRepo();
        var service = new BusinessReportService(repo);
        await service.GenerateAsync(new("Week", new DateOnly(2026, 10, 7)), Manager, null);

        var again = await service.GenerateAsync(new("Week", new DateOnly(2026, 10, 9)), Manager, null);
        Assert.Equal("BUSINESS_REPORT_EXISTS", again.ErrorCode);
        Assert.Equal(409, again.StatusCode);

        var confirmed = await service.GenerateAsync(new("Week", new DateOnly(2026, 10, 9), ConfirmNew: true), Manager, null);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal(2, repo.Reports.Count);
        Assert.Equal(2, repo.Audits.Count);
    }

    [Fact]
    public async Task UnknownPeriodTypeAndReportAreRejected()
    {
        var service = new BusinessReportService(new ReportRepo());
        Assert.Equal("BUSINESS_REPORT_INVALID_PERIOD", (await service.GenerateAsync(new("Year", new DateOnly(2026, 1, 1)), Manager, null)).ErrorCode);
        Assert.Equal(404, (await service.GetReportAsync(42)).StatusCode);
        Assert.False(new GenerateBusinessReportRequestValidator().Validate(new GenerateBusinessReportRequest("Quarter", new DateOnly(2026, 1, 1))).IsValid);
    }

    [Theory]
    [InlineData("Day", "2026-11-04", "2026-11-03", "2026-11-03")]
    [InlineData("Week", "2026-10-26", "2026-10-19", "2026-10-25")]
    [InlineData("Month", "2026-03-01", "2026-02-01", "2026-02-28")]
    [InlineData("Month", "2026-01-01", "2025-12-01", "2025-12-31")]
    public void PreviousPeriodIsTheDayWeekOrMonthBefore(string type, string start, string prevStart, string prevEnd) =>
        Assert.Equal((DateOnly.Parse(prevStart), DateOnly.Parse(prevEnd)), BusinessReportService.PreviousPeriod(type, DateOnly.Parse(start)));

    [Fact]
    public async Task ReportComparesWithTheSavedReportOfThePreviousMonth()
    {
        var repo = new ReportRepo { SourceFor = start => Revenue(start.Month == 8 ? 3_000_000m : 4_500_000m) };
        var service = new BusinessReportService(repo);
        await service.GenerateAsync(new("Month", new DateOnly(2026, 8, 5)), Manager, null);
        repo.SourceFor = start => Revenue(start.Month == 8 ? 9_999_999m : 4_500_000m); // August changed after it was saved

        var september = (await service.GenerateAsync(new("Month", new DateOnly(2026, 9, 1)), Manager, null)).Data!;

        var revenue = september.Metrics.Single(m => m.MetricType == "TotalRevenue");
        Assert.Equal(4_500_000m, revenue.Value);
        Assert.Equal(3_000_000m, revenue.PreviousValue); // the saved August report, not today's data
        Assert.Equal(BusinessReportResponse.FromSnapshot, september.PreviousSource);
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), (september.PreviousPeriodStart, september.PreviousPeriodEnd));
    }

    [Fact]
    public async Task WithoutASavedPreviousReportThePreviousPeriodIsCalculated()
    {
        var repo = new ReportRepo { SourceFor = start => Revenue(start.Day == 2 ? 2_000_000m : 500_000m) };
        var service = new BusinessReportService(repo);
        var created = (await service.GenerateAsync(new("Day", new DateOnly(2026, 11, 2)), Manager, null)).Data!;

        var report = (await service.GetReportAsync(created.Id)).Data!;

        Assert.Equal(BusinessReportResponse.FromCurrentData, report.PreviousSource);
        Assert.Equal(500_000m, report.Metrics.Single(m => m.MetricType == "TotalRevenue").PreviousValue);
        Assert.Contains((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1)), repo.SourceCalls);
    }

    [Fact]
    public async Task TrendListsConsecutivePeriodsOldestFirst()
    {
        var repo = new ReportRepo { SourceFor = start => Revenue(start.Month * 1_000_000m) };

        var trend = (await new BusinessReportService(repo).GetTrendAsync(new("Month", new DateOnly(2026, 6, 15), 4))).Data!;

        Assert.Equal("Month", trend.PeriodType);
        Assert.Equal([new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1)],
            trend.Points.Select(p => p.PeriodStart));
        Assert.Equal(new DateOnly(2026, 6, 30), trend.Points[^1].PeriodEnd);
        Assert.Equal([3_000_000m, 4_000_000m, 5_000_000m, 6_000_000m],
            trend.Points.Select(p => p.Metrics.Single(m => m.MetricType == "TotalRevenue").Value));
        Assert.Empty(repo.Reports); // a trend saves nothing
    }

    [Fact]
    public async Task TrendRejectsAPeriodThatHasNotEnded()
    {
        var service = new BusinessReportService(new ReportRepo());
        Assert.Equal("BUSINESS_REPORT_PERIOD_IN_FUTURE", (await service.GetTrendAsync(new("Week", GymToday.AddDays(8)))).ErrorCode);
        Assert.Equal("BUSINESS_REPORT_INVALID_PERIOD", (await service.GetTrendAsync(new("Year", new DateOnly(2026, 1, 1)))).ErrorCode);
    }

    [Theory]
    [InlineData("Month", 6, true)]
    [InlineData("week", 12, true)]
    [InlineData("Day", 1, false)]
    [InlineData("Day", 13, false)]
    [InlineData("Quarter", 6, false)]
    public void TrendValidatorChecksTypeAndCount(string type, int count, bool valid) =>
        Assert.Equal(valid, new BusinessReportTrendRequestValidator().Validate(new BusinessReportTrendRequest(type, new DateOnly(2026, 6, 1), count)).IsValid);

    private static ReportSourceData Revenue(decimal amount) => new(amount, 0, 0, 0, 0, 0, 0, 0, 0);

    // ── Fakes ────────────────────────────────────────────────────

    private abstract class AuditedFake : IAuditedRepository
    {
        protected int NextId = 1;
        public List<AuditLog> Audits { get; } = [];

        public virtual void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity.Id == 0) entity.Id = NextId++;
            if (entity is AuditLog a) Audits.Add(a);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class GymRepo : AuditedFake, IGymSettingsRepository
    {
        private readonly Gym _gym = new() { Id = 1, GymName = "Old Gym", IsAcceptingMembers = true };
        public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) => Task.FromResult<Gym?>(_gym);
    }

    private sealed class ReportRepo : AuditedFake, IBusinessReportRepository
    {
        public List<BusinessReportSnapshot> Reports { get; } = [];
        public ReportSourceData Source { get; set; } = new(100m, 0, 0, 0, 0, 0, 0, 0, 0);
        /// <summary>When set, the source data depends on the period start (to tell periods apart).</summary>
        public Func<DateOnly, ReportSourceData>? SourceFor { get; set; }
        public List<(DateOnly Start, DateOnly End)> SourceCalls { get; } = [];
        public DateTime LastStartUtc { get; private set; }
        public DateTime LastEndUtc { get; private set; }

        public override void Add<TEntity>(TEntity entity)
        {
            base.Add(entity);
            if (entity is BusinessReportSnapshot r) Reports.Add(r);
        }

        public Task<(IReadOnlyList<BusinessReportSnapshot> Items, int TotalCount)> SearchAsync(
            DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<BusinessReportSnapshot>, int)>((Reports, Reports.Count));

        public Task<BusinessReportSnapshot?> GetAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Reports.FirstOrDefault(r => r.Id == id));

        public Task<BusinessReportSnapshot?> FindByPeriodAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(Reports.LastOrDefault(r => r.PeriodStart == start && r.PeriodEnd == end));

        public Task<ReportSourceData> GetSourceDataAsync(
            DateOnly start, DateOnly end, DateTime startUtc, DateTime endUtcExclusive, CancellationToken cancellationToken = default)
        {
            if (SourceCalls.Count == 0) // the report's own period; later calls are for the comparison
            {
                LastStartUtc = startUtc;
                LastEndUtc = endUtcExclusive;
            }
            SourceCalls.Add((start, end));
            return Task.FromResult(SourceFor?.Invoke(start) ?? Source);
        }
    }
}
