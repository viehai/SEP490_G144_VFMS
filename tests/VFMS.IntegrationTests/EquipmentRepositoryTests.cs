using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>Equipment and issue lists (filters, sorts, search) and issue counts against a real SQLite database.</summary>
public sealed class EquipmentRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new SqliteTestContext(_options);
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Equipment test gym" };
        db.Equipments.AddRange(
            Item(gym, "Treadmill T5", "TM-0002", "Life Fitness", new DateOnly(2024, 5, 1), new DateTime(2025, 1, 1)),
            Item(gym, "Bench Press", "ST-0001", "Hammer Strength", new DateOnly(2025, 3, 1), new DateTime(2025, 2, 1)),
            Item(gym, "Assault Bike", "CD-0001", "Life Fitness", null, new DateTime(2025, 3, 1)),
            Item(gym, "Yoga Mat", null, null, new DateOnly(2023, 1, 1), new DateTime(2025, 4, 1)),
            Item(gym, "Cable Station", "ST-0002", "", null, new DateTime(2025, 5, 1)));
        var member = new ApplicationUser
        {
            UserId = "member-1", Gym = gym, Email = "m1@example.com", FullName = "Member", PasswordHash = "x",
            AccountStatus = AccountStatus.Active,
        };
        db.Add(member);
        await db.SaveChangesAsync();

        var treadmill = await db.Equipments.SingleAsync(e => e.EquipmentName == "Treadmill T5");
        var bench = await db.Equipments.SingleAsync(e => e.EquipmentName == "Bench Press");
        db.EquipmentIssueReports.AddRange(
            Issue(treadmill, member, IncidentType.Malfunction, "Belt slips", new DateTime(2025, 6, 5)),
            Issue(bench, member, IncidentType.Safety, "Loose bolt", new DateTime(2025, 6, 1)),
            Issue(treadmill, member, IncidentType.Other, "Dusty", new DateTime(2025, 6, 6)),
            Issue(bench, member, IncidentType.Damage, "Torn pad", new DateTime(2025, 6, 2)),
            Issue(treadmill, member, IncidentType.Safety, "Emergency stop broken", new DateTime(2025, 6, 3)));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static Equipment Item(Gym gym, string name, string? code, string? brand, DateOnly? purchased, DateTime created) => new()
    {
        Gym = gym, EquipmentName = name, EquipmentCode = code, EquipmentBrand = brand, PurchaseDate = purchased, CreatedAt = created,
    };

    private static EquipmentIssueReport Issue(Equipment equipment, ApplicationUser user, IncidentType type, string text, DateTime at) => new()
    {
        Equipment = equipment, ReportedByUser = user, IncidentType = type, Description = text, ReportedAt = at,
    };

    private async Task<string[]> Issues(EquipmentIssueSort sort, string? search = null)
    {
        await using var db = new SqliteTestContext(_options);
        var (items, total) = await new EquipmentRepository(db).SearchIssuesAsync(
            new EquipmentIssueQuery(null, null, null, search, sort, 1, 20));
        Assert.Equal(items.Count, total);
        return items.Select(i => i.Description).ToArray();
    }

    private async Task<string[]> Names(string? brand = null, EquipmentSort sort = EquipmentSort.Name, string? search = null)
    {
        await using var db = new SqliteTestContext(_options);
        var (items, _) = await new EquipmentRepository(db).SearchEquipmentAsync(
            new EquipmentQuery(search, null, null, brand, sort, 1, 20));
        return items.Select(e => e.EquipmentName).ToArray();
    }

    [Fact]
    public async Task BrandFilterIsExact()
    {
        Assert.Equal(["Assault Bike", "Treadmill T5"], await Names(brand: "Life Fitness"));
        Assert.Empty(await Names(brand: "Life"));
    }

    [Fact]
    public async Task SortsByNameByDefault() =>
        Assert.Equal(["Assault Bike", "Bench Press", "Cable Station", "Treadmill T5", "Yoga Mat"], await Names());

    [Fact]
    public async Task CodeSortPutsEquipmentWithoutCodeLast() =>
        Assert.Equal(["Assault Bike", "Bench Press", "Cable Station", "Treadmill T5", "Yoga Mat"], await Names(sort: EquipmentSort.Code));

    [Fact]
    public async Task NewestSortUsesTheDateAdded() =>
        Assert.Equal("Cable Station", (await Names(sort: EquipmentSort.Newest))[0]);

    [Fact]
    public async Task PurchaseDateSortIsNewestFirstWithUndatedLast() =>
        Assert.Equal(["Bench Press", "Treadmill T5", "Yoga Mat", "Assault Bike", "Cable Station"], await Names(sort: EquipmentSort.PurchaseDate));

    [Fact]
    public async Task SearchStillMatchesNameOrCode()
    {
        Assert.Equal(["Bench Press", "Cable Station"], await Names(search: "ST-"));
        Assert.Equal(["Treadmill T5"], await Names(search: "Tread"));
    }

    [Fact]
    public async Task BrandsAreDistinctSortedAndSkipBlank()
    {
        await using var db = new SqliteTestContext(_options);
        Assert.Equal(["Hammer Strength", "Life Fitness"], await new EquipmentRepository(db).GetBrandsAsync());
    }

    [Fact]
    public async Task SeveritySortPutsSafetyFirstThenDamageMalfunctionOther() =>
        Assert.Equal(["Emergency stop broken", "Loose bolt", "Torn pad", "Belt slips", "Dusty"], await Issues(EquipmentIssueSort.Severity));

    [Fact]
    public async Task NewestSortIsByReportTime() =>
        Assert.Equal(["Dusty", "Belt slips", "Emergency stop broken", "Torn pad", "Loose bolt"], await Issues(EquipmentIssueSort.Newest));

    [Fact]
    public async Task OpenOnlyKeepsInProgressAndEscalatedIssues()
    {
        await using (var db = new SqliteTestContext(_options))
        {
            (await db.EquipmentIssueReports.SingleAsync(i => i.Description == "Dusty")).IssueStatus = EquipmentIssueStatus.Resolved;
            (await db.EquipmentIssueReports.SingleAsync(i => i.Description == "Torn pad")).IssueStatus = EquipmentIssueStatus.Resolved;
            (await db.EquipmentIssueReports.SingleAsync(i => i.Description == "Belt slips")).IssueStatus = EquipmentIssueStatus.InProgress;
            (await db.EquipmentIssueReports.SingleAsync(i => i.Description == "Loose bolt")).IssueStatus = EquipmentIssueStatus.Escalated;
            await db.SaveChangesAsync();
        }

        await using var read = new SqliteTestContext(_options);
        var (items, total) = await new EquipmentRepository(read).SearchIssuesAsync(
            new EquipmentIssueQuery(null, null, null, null, EquipmentIssueSort.Severity, 1, 20, OpenOnly: true));

        // Safety first (newest first within it), then Malfunction; Resolved issues are left out.
        Assert.Equal(["Emergency stop broken", "Loose bolt", "Belt slips"], items.Select(i => i.Description).ToArray());
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task IssueSearchMatchesEquipmentNameOrCode()
    {
        Assert.Equal(["Loose bolt", "Torn pad"], await Issues(EquipmentIssueSort.Severity, "ST-0001"));
        Assert.Equal(3, (await Issues(EquipmentIssueSort.Newest, "Treadmill")).Length);
        Assert.Empty(await Issues(EquipmentIssueSort.Newest, "Rower"));
    }

    [Fact]
    public async Task IssueStatsCountOpenRecentAndSafety()
    {
        await using var db = new SqliteTestContext(_options);
        var treadmill = await db.Equipments.SingleAsync(e => e.EquipmentName == "Treadmill T5");
        var dusty = await db.EquipmentIssueReports.SingleAsync(i => i.Description == "Dusty");
        dusty.IssueStatus = EquipmentIssueStatus.Resolved;
        dusty.ResolvedAt = new DateTime(2025, 6, 7);
        await db.SaveChangesAsync();

        var stats = await new EquipmentRepository(db).GetIssueStatsAsync(treadmill.Id, new DateTime(2025, 6, 4));

        Assert.Equal(3, stats.Total);
        Assert.Equal(2, stats.Open);
        Assert.Equal(2, stats.Recent);      // Belt slips (5/6) and Dusty (6/6)
        Assert.Equal(1, stats.Safety);
        Assert.Equal(new DateTime(2025, 6, 6), stats.LastReportedAt);
        Assert.Equal(new DateTime(2025, 6, 7), stats.LastClosedAt);
    }

    [Fact]
    public async Task EquipmentWithoutIssuesHasEmptyStats()
    {
        await using var db = new SqliteTestContext(_options);
        var mat = await db.Equipments.SingleAsync(e => e.EquipmentName == "Yoga Mat");

        var stats = await new EquipmentRepository(db).GetIssueStatsAsync(mat.Id, new DateTime(2025, 1, 1));

        Assert.Equal(new EquipmentIssueStats(0, 0, 0, 0, null, null), stats);
    }

    [Fact]
    public async Task RecentIssueCountsArePerEquipmentAndSkipTheRest()
    {
        await using var db = new SqliteTestContext(_options);
        var ids = await db.Equipments.Select(e => e.Id).ToArrayAsync();
        var names = await db.Equipments.ToDictionaryAsync(e => e.Id, e => e.EquipmentName);

        var counts = await new EquipmentRepository(db).CountRecentIssuesAsync(ids, new DateTime(2025, 6, 2));

        Assert.Equal(new Dictionary<string, int> { ["Treadmill T5"] = 3, ["Bench Press"] = 1 },
            counts.ToDictionary(c => names[c.Key], c => c.Value));
    }

    [Fact]
    public async Task IssuesAreCountedPerStatus()
    {
        await using (var setup = new SqliteTestContext(_options))
        {
            var dusty = await setup.EquipmentIssueReports.SingleAsync(i => i.Description == "Dusty");
            dusty.IssueStatus = EquipmentIssueStatus.Resolved;
            await setup.SaveChangesAsync();
        }
        await using var db = new SqliteTestContext(_options);

        var counts = await new EquipmentRepository(db).CountIssuesByStatusAsync(null);

        Assert.Equal(4, counts[EquipmentIssueStatus.InProgress]);
        Assert.Equal(1, counts[EquipmentIssueStatus.Resolved]);
        Assert.False(counts.ContainsKey(EquipmentIssueStatus.Escalated));
        Assert.Empty(await new EquipmentRepository(db).CountIssuesByStatusAsync("someone-else"));
    }
}
