using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>Equipment and issue lists (filters, sorts, search) against a real SQLite database.</summary>
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
    public async Task IssueSearchMatchesEquipmentNameOrCode()
    {
        Assert.Equal(["Loose bolt", "Torn pad"], await Issues(EquipmentIssueSort.Severity, "ST-0001"));
        Assert.Equal(3, (await Issues(EquipmentIssueSort.Newest, "Treadmill")).Length);
        Assert.Empty(await Issues(EquipmentIssueSort.Newest, "Rower"));
    }
}
