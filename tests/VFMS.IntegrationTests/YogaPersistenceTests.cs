using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>Runs the yoga flow against the real EF model, so mapping errors (wrong FKs, indexes) fail here, not in production.</summary>
public sealed class YogaPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new SqliteTestContext(_options);
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Yoga test gym" };
        var role = new ApplicationRole { RoleName = YogaClassService.InstructorRole };
        // A Yoga Instructor created by an Admin: no TrainerProfile row.
        var instructor = new ApplicationUser
        {
            UserId = "yi-1", Gym = gym, Email = "yi@example.com", FullName = "Lan Yoga", PasswordHash = "x",
            AccountStatus = AccountStatus.Active
        };
        db.AddRange(gym, role, instructor, new ApplicationUser
        {
            UserId = "manager", Gym = gym, Email = "m@example.com", FullName = "Manager", PasswordHash = "x",
            AccountStatus = AccountStatus.Active
        });
        db.ApplicationUserRoles.Add(new ApplicationUserRole { User = instructor, Role = role });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    /// <summary>Studio, type, class (instructor yi-1), one session in 10 days, published.</summary>
    private static async Task<int> CreatePublishedClassAsync(VfmsDbContext db, int max)
    {
        var repository = new YogaRepository(db);
        var studios = new YogaStudioService(repository, TimeProvider.System);
        var classes = new YogaClassService(repository, TimeProvider.System);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var studio = await studios.CreateAsync(new YogaStudioRequest("Lotus", 2, 20), "manager", null);
        var type = await classes.CreateClassTypeAsync(new YogaClassTypeRequest("Vinyasa", null), "manager", null);
        var yogaClass = await classes.CreateClassAsync(new YogaClassRequest(
            type.Data!.Id, "Morning Flow", "yi-1", "Beginner", 2, 1, max, 60,
            DateTime.UtcNow.AddDays(5), today.AddDays(1), today.AddDays(40)), "manager", null);
        await classes.CreateSessionAsync(new YogaSessionRequest(
            yogaClass.Data!.Id, studio.Data!.Id, today.AddDays(10), new(9, 0), new(10, 0)), "manager", null);
        var published = await classes.PublishClassAsync(yogaClass.Data.Id, "manager", null);
        Assert.True(published.IsSuccess, published.ErrorMessage);
        return yogaClass.Data.Id;
    }

    [Fact]
    public async Task MembersBookMatsUntilFullThenJoinTheWaitlist()   // SCRUM-106
    {
        await using var db = new SqliteTestContext(_options);
        var gym = await db.Gyms.FirstAsync();
        foreach (var id in new[] { "b1", "b2", "b3" })
            db.AddRange(new ApplicationUser { UserId = id, Gym = gym, Email = id + "@example.com", FullName = id, PasswordHash = "x", AccountStatus = AccountStatus.Active },
                TestSeed.Membership(id, ComponentType.YOGA));
        await db.SaveChangesAsync();
        var classId = await CreatePublishedClassAsync(db, max: 2);
        var service = new YogaBookingService(new YogaBookingRepository(db), TimeProvider.System);

        var first = await service.BookAsync("b1", new YogaBookingRequest(classId, 2));
        var second = await service.BookAsync("b2", new YogaBookingRequest(classId));
        var third = await service.BookAsync("b3", new YogaBookingRequest(classId));

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal(1, second.Data!.MatNumber);   // lowest free mat
        Assert.Equal(("Waitlisted", 1), (third.Data!.Status, third.Data.WaitlistPosition!.Value));
        Assert.Equal(YogaClassStatus.Full, (await db.YogaClasses.AsNoTracking().SingleAsync()).ClassStatus);
        Assert.Equal(2, await db.YogaBookings.CountAsync(b => b.BookingStatus == YogaBookingStatus.Confirmed));
        Assert.Equal("b3", (await db.YogaWaitlists.AsNoTracking().SingleAsync()).MemberUserId);
    }

    [Fact]
    public async Task ManagerCanCreateAndPublishClassForInstructorWithoutTrainerProfile()   // SCRUM-103/104
    {
        await using var db = new SqliteTestContext(_options);
        var repository = new YogaRepository(db);
        var studios = new YogaStudioService(repository, TimeProvider.System);
        var classes = new YogaClassService(repository, TimeProvider.System);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        var studio = await studios.CreateAsync(new YogaStudioRequest("Lotus", 2, 20), "manager", null);
        var type = await classes.CreateClassTypeAsync(new YogaClassTypeRequest("Vinyasa", null), "manager", null);
        var yogaClass = await classes.CreateClassAsync(new YogaClassRequest(
            type.Data!.Id, "Morning Flow", "yi-1", "Beginner", 2, 3, 15, 60,
            DateTime.UtcNow.AddDays(5), today.AddDays(1), today.AddDays(40)), "manager", null);
        var session = await classes.CreateSessionAsync(new YogaSessionRequest(
            yogaClass.Data!.Id, studio.Data!.Id, today.AddDays(10), new(9, 0), new(10, 0)), "manager", null);
        var published = await classes.PublishClassAsync(yogaClass.Data.Id, "manager", null);

        Assert.True(session.IsSuccess, session.ErrorMessage);
        Assert.True(published.IsSuccess, published.ErrorMessage);
        Assert.Equal("Open", published.Data!.Status);
        Assert.Equal("yi-1", (await db.YogaClasses.AsNoTracking().SingleAsync()).InstructorUserId);
    }
}
