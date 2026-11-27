using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>GBR-128..GBR-130 against the real EF model: request rows, FKs, and approval creating the class in one transaction.</summary>
public sealed class YogaClassOpeningPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;
    private int _typeId, _studioId;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new SqliteTestContext(_options);
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Yoga request gym" };
        var instructorRole = new ApplicationRole { RoleName = YogaClassService.InstructorRole };
        var managerRole = new ApplicationRole { RoleName = "Manager" };
        var instructor = User("yi-1", gym, "0901000020");
        var manager = User("manager", gym, "0901000021");
        var type = new YogaClassType { TypeName = "Hatha" };
        var studio = new YogaStudio { Gym = gym, StudioName = "Lotus", FloorNumber = 2, Capacity = 20, IsActive = true };
        db.AddRange(gym, instructorRole, managerRole, instructor, manager, type, studio);
        db.ApplicationUserRoles.AddRange(
            new ApplicationUserRole { User = instructor, Role = instructorRole },
            new ApplicationUserRole { User = manager, Role = managerRole });
        await db.SaveChangesAsync();
        (_typeId, _studioId) = (type.Id, studio.Id);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static ApplicationUser User(string id, Gym gym, string phone) => new()
    {
        UserId = id, Gym = gym, Email = id + "@example.com", FullName = id == "yi-1" ? "Lan Yoga" : "Manager", PasswordHash = "x",
        PhoneNumber = phone, AccountStatus = AccountStatus.Active
    };

    private static YogaClassOpeningService Service(VfmsDbContext db)
    {
        var yoga = new YogaRepository(db);
        return new YogaClassOpeningService(new YogaClassOpeningRepository(db), yoga, new YogaClassService(yoga, TimeProvider.System), TimeProvider.System);
    }

    private YogaClassOpeningRequestRequest Request() => new(
        _typeId, "Sunrise Flow", "Beginner", 2, 3, 15, 60, Today.AddDays(10), Today.AddDays(50),
        [DayOfWeek.Tuesday, DayOfWeek.Thursday], new TimeOnly(6, 0), _studioId, "Gentle", "Morning demand");

    [Fact]
    public async Task InstructorSubmitsAndManagerApprovesIntoADraftClass()
    {
        int requestId;
        await using (var db = new SqliteTestContext(_options))
        {
            var submitted = await Service(db).SubmitAsync("yi-1", Request(), null);
            Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
            Assert.Equal(("Lan Yoga", "Hatha", "Lotus"), (submitted.Data!.InstructorName, submitted.Data.TypeName, submitted.Data.PreferredStudioName));
            requestId = submitted.Data.Id;
        }

        await using (var db = new SqliteTestContext(_options))
        {
            var form = new YogaClassRequest(_typeId, "Sunrise Flow", "yi-1", "Beginner", 2, 3, 15, 60,
                DateTime.UtcNow.AddDays(8), Today.AddDays(10), Today.AddDays(50), "Gentle");
            var approved = await Service(db).ApproveAsync(requestId, form, "manager", "127.0.0.1");
            Assert.True(approved.IsSuccess, approved.ErrorMessage);
        }

        await using (var db = new SqliteTestContext(_options))
        {
            var request = await db.YogaClassOpeningRequests.SingleAsync();
            var yogaClass = await db.YogaClasses.SingleAsync();
            Assert.Equal(YogaClassRequestStatus.Approved, request.RequestStatus);
            Assert.Equal(yogaClass.Id, request.YogaClassId);
            Assert.Equal("manager", request.ReviewedByUserId);
            Assert.Equal(YogaClassStatus.Draft, yogaClass.ClassStatus);
            Assert.Equal("yi-1", yogaClass.InstructorUserId);
            Assert.True(await db.Notifications.AnyAsync(n => n.RecipientUserId == "manager" && n.NotificationType == "YogaClassRequest"));
            Assert.True(await db.Notifications.AnyAsync(n => n.RecipientUserId == "yi-1" && n.NotificationType == "YogaClassRequest"));
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.AuditEntity == nameof(YogaClassOpeningRequest)));   // submit + approve
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.AuditEntity == nameof(YogaClass)));
        }
    }

    [Fact]
    public async Task RejectedRequestKeepsTheReasonAndCreatesNoClass()
    {
        await using var db = new SqliteTestContext(_options);
        var service = Service(db);
        var id = (await service.SubmitAsync("yi-1", Request(), null)).Data!.Id;

        var rejected = await service.RejectAsync(id, "Lotus is under repair", "manager", null);
        var mine = await service.GetMineAsync("yi-1");

        Assert.True(rejected.IsSuccess, rejected.ErrorMessage);
        Assert.Equal("Manager", rejected.Data!.ReviewedByName);
        var row = Assert.Single(mine.Data!);
        Assert.Equal(("Rejected", "Lotus is under repair"), (row.Status, row.RejectReason));
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Thursday], row.PreferredDays);
        Assert.Empty(db.YogaClasses);
    }

    [Fact]
    public async Task FailedApprovalRollsNothingForward()
    {
        await using var db = new SqliteTestContext(_options);
        var service = Service(db);
        var id = (await service.SubmitAsync("yi-1", Request(), null)).Data!.Id;
        var badForm = new YogaClassRequest(_typeId, "Sunrise Flow", "not-an-instructor", "Beginner", 2, 3, 15, 60,
            DateTime.UtcNow.AddDays(8), Today.AddDays(10), Today.AddDays(50));

        var result = await service.ApproveAsync(id, badForm, "manager", null);

        Assert.Equal("YOGA_INVALID_INSTRUCTOR", result.ErrorCode);
        Assert.Empty(db.YogaClasses);
        Assert.Equal(YogaClassRequestStatus.Pending, (await db.YogaClassOpeningRequests.AsNoTracking().SingleAsync()).RequestStatus);
    }
}
