using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Workout;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>The Manager's default machine on an exercise, saved through the real repository and SQLite.</summary>
public sealed class ExerciseMachineTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;
    private int _brokenBike, _workingBike, _legPress;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new SqliteTestContext(_options);
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Machine test gym" };
        var brokenBike = new Equipment { Gym = gym, EquipmentName = "Xe đạp tập", EquipmentCode = "CD-0003", EquipmentStatus = EquipmentStatus.Damaged };
        var workingBike = new Equipment { Gym = gym, EquipmentName = "Xe đạp tập ", EquipmentCode = "CD-0004" };
        var legPress = new Equipment { Gym = gym, EquipmentName = "Máy đạp đùi", EquipmentCode = "ST-0001" };
        db.Equipments.AddRange(brokenBike, workingBike, legPress);
        db.Add(new ApplicationUser
        {
            UserId = "manager-1", Gym = gym, Email = "manager@example.com", FullName = "Manager", PasswordHash = "x",
            AccountStatus = AccountStatus.Active,
        });
        await db.SaveChangesAsync();
        (_brokenBike, _workingBike, _legPress) = (brokenBike.Id, workingBike.Id, legPress.Id);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static ExerciseCatalogService Service(VfmsDbContext db) => new(new ExerciseCatalogRepository(db));

    [Fact]
    public async Task MachinePickerHasOneOptionPerNamePointingAtAWorkingUnit()
    {
        await using var db = new SqliteTestContext(_options);
        var options = (await Service(db).ListMachinesAsync()).Data!;

        Assert.Equal(2, options.Count);
        var bike = Assert.Single(options, o => o.Name == "Xe đạp tập");
        Assert.Equal((_workingBike, 2, 1), (bike.Id, bike.Units, bike.WorkingUnits));
    }

    [Fact]
    public async Task ExerciseSavesItsMachineWithoutAddingEquipmentAndCanChangeOrClearIt()
    {
        int id;
        await using (var db = new SqliteTestContext(_options))
        {
            var created = await Service(db).CreateAsync(
                new ExerciseRequest("Stationary Bike", "Cardio", "Beginner", EquipmentId: _brokenBike), "manager-1", null);
            Assert.True(created.IsSuccess, created.ErrorMessage);
            Assert.Equal((_brokenBike, "Xe đạp tập"), (created.Data!.EquipmentId, created.Data.EquipmentName));
            id = created.Data.Id;
        }
        await using (var db = new SqliteTestContext(_options))
        {
            Assert.Equal(3, await db.Equipments.CountAsync());
            var updated = await Service(db).UpdateAsync(id,
                new ExerciseRequest("Stationary Bike", "Cardio", "Beginner", EquipmentId: _legPress), "manager-1", null);
            Assert.True(updated.IsSuccess, updated.ErrorMessage);
        }
        await using (var db = new SqliteTestContext(_options))
        {
            var listed = Assert.Single((await Service(db).ListAsync()).Data!);
            Assert.Equal((_legPress, "Máy đạp đùi"), (listed.EquipmentId, listed.EquipmentName));
            await Service(db).UpdateAsync(id, new ExerciseRequest("Stationary Bike", "Cardio", "Beginner"), "manager-1", null);
        }
        await using (var db = new SqliteTestContext(_options))
            Assert.Null((await db.Exercises.SingleAsync()).EquipmentId);
    }

    [Fact]
    public async Task UnknownMachineIsRejected()
    {
        await using var db = new SqliteTestContext(_options);
        var result = await Service(db).CreateAsync(
            new ExerciseRequest("Stationary Bike", "Cardio", "Beginner", EquipmentId: 9999), "manager-1", null);

        Assert.Equal("EXERCISE_MACHINE_NOT_FOUND", result.ErrorCode);
        Assert.Equal(0, await db.Exercises.CountAsync());
    }

    [Fact]
    public async Task ActiveExercisesCarryTheirMachineForPlanDefaults()
    {
        await using (var db = new SqliteTestContext(_options))
            await Service(db).CreateAsync(new ExerciseRequest("Stationary Bike", "Cardio", "Beginner", EquipmentId: _brokenBike), "manager-1", null);
        await using (var db = new SqliteTestContext(_options))
        {
            var repo = new WorkoutRepository(db);
            var exercises = await repo.GetActiveExercisesAsync();
            var defaults = WorkoutPlanService.DefaultMachines(exercises, await repo.GetUsableEquipmentAsync());
            // The picked unit is broken, so another working unit with the same name stands in.
            Assert.Equal(_workingBike, defaults[exercises.Single().Id]);
        }
    }
}
