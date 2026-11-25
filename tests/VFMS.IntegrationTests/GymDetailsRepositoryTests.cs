using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>UC-70 facility counts against a real SQLite database.</summary>
public sealed class GymDetailsRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;
    private int _gymId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new SqliteTestContext(_options);
        await db.Database.EnsureCreatedAsync();

        var gym = new Gym { GymName = "Victor Fitness" };
        var other = new Gym { GymName = "Other gym" };
        var cardio = new CatalogCategory { CategoryName = "Cardio", Description = "Treadmills and bikes" };
        var strength = new CatalogCategory { CategoryName = "Bench & Rack" };
        db.Equipments.AddRange(
            Machine(gym, cardio, "Treadmill 1", EquipmentStatus.Operational),
            Machine(gym, cardio, "Treadmill 2", EquipmentStatus.UnderMaintenance),
            Machine(gym, cardio, "Old bike", EquipmentStatus.Retired),
            Machine(gym, strength, "Bench", EquipmentStatus.Damaged),
            Machine(gym, null, "Foam roller", EquipmentStatus.Operational),
            Machine(other, cardio, "Treadmill elsewhere", EquipmentStatus.Operational));
        db.YogaStudios.AddRange(
            new YogaStudio { Gym = gym, StudioName = "Lotus", FloorNumber = 3, Capacity = 20 },
            new YogaStudio { Gym = gym, StudioName = "Bamboo", FloorNumber = 2, Capacity = 15 },
            new YogaStudio { Gym = gym, StudioName = "Closed room", FloorNumber = 1, Capacity = 10, IsActive = false },
            new YogaStudio { Gym = other, StudioName = "Elsewhere", FloorNumber = 1, Capacity = 10 });
        db.LockerZones.AddRange(
            new LockerZone { Gym = gym, ZoneName = "B", FloorNumber = 1, GenderRestriction = GenderRestriction.Male, TotalLockers = 30 },
            new LockerZone { Gym = gym, ZoneName = "A", FloorNumber = 1, GenderRestriction = GenderRestriction.Female, TotalLockers = 40 },
            new LockerZone { Gym = other, ZoneName = "Z", FloorNumber = 1, TotalLockers = 5 });
        await db.SaveChangesAsync();
        _gymId = gym.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static Equipment Machine(Gym gym, CatalogCategory? category, string name, EquipmentStatus status) =>
        new() { Gym = gym, CatalogCategory = category, EquipmentName = name, EquipmentStatus = status };

    [Fact]
    public async Task EquipmentIsCountedPerCategoryWithoutRetiredOrOtherGyms()
    {
        await using var db = new SqliteTestContext(_options);
        var areas = await new GymDetailsRepository(db).GetEquipmentAreasAsync(_gymId);

        Assert.Equal(
            [("Bench & Rack", 1, 0), ("Cardio", 2, 1), (null, 1, 1)],
            areas.Select(a => (a.CategoryName, a.Total, a.Available)).ToArray());
        Assert.Equal("Treadmills and bikes", areas[1].Description);
    }

    [Fact]
    public async Task OnlyActiveStudiosOfTheGymInFloorOrder()
    {
        await using var db = new SqliteTestContext(_options);
        var studios = await new GymDetailsRepository(db).GetActiveStudiosAsync(_gymId);

        Assert.Equal(["Bamboo", "Lotus"], studios.Select(s => s.StudioName).ToArray());
    }

    [Fact]
    public async Task LockerZonesOfTheGymInFloorAndNameOrder()
    {
        await using var db = new SqliteTestContext(_options);
        var zones = await new GymDetailsRepository(db).GetLockerZonesAsync(_gymId);

        Assert.Equal(["A", "B"], zones.Select(z => z.ZoneName).ToArray());
    }

    [Fact]
    public async Task DefaultGymIsTheFirstOne()
    {
        await using var db = new SqliteTestContext(_options);
        var gym = await new GymDetailsRepository(db).GetDefaultGymAsync();

        Assert.Equal(_gymId, gym!.Id);
    }
}
