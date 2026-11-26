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
            PhoneNumber = "0901000010", AccountStatus = AccountStatus.Active
        };
        db.AddRange(gym, role, instructor, new ApplicationUser
        {
            UserId = "manager", Gym = gym, Email = "m@example.com", FullName = "Manager", PasswordHash = "x",
            PhoneNumber = "0901000011", AccountStatus = AccountStatus.Active
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
            db.AddRange(new ApplicationUser { UserId = id, Gym = gym, Email = id + "@example.com", FullName = id, PhoneNumber = "090100002" + id[1], PasswordHash = "x", AccountStatus = AccountStatus.Active },
                TestSeed.Membership(id, ComponentType.YOGA));
        await db.SaveChangesAsync();
        var classId = await CreatePublishedClassAsync(db, max: 2);
        var service = new YogaBookingService(new YogaBookingRepository(db), TimeProvider.System);

        var first = await service.BookAsync("b1", new YogaBookingRequest(classId, 2));
        var second = await service.BookAsync("b2", new YogaBookingRequest(classId));
        var third = await service.BookAsync("b3", new YogaBookingRequest(classId));

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal(await db.MemberMemberships.Where(m => m.MemberUserId == "b1").Select(m => m.OrderItemId).SingleAsync(),
            (await db.YogaBookings.SingleAsync(b => b.MemberUserId == "b1")).OrderItemId);
        Assert.Equal(1, second.Data!.MatNumber);   // lowest free mat
        Assert.Equal(("Waitlisted", 1), (third.Data!.Status, third.Data.WaitlistPosition!.Value));
        Assert.Equal(YogaClassStatus.Full, (await db.YogaClasses.AsNoTracking().SingleAsync()).ClassStatus);
        Assert.Equal(2, await db.YogaBookings.CountAsync(b => b.BookingStatus == YogaBookingStatus.Confirmed));
        Assert.Equal("b3", (await db.YogaWaitlists.AsNoTracking().SingleAsync()).MemberUserId);

        var cancelled = await service.CancelAsync("b1", first.Data!.BookingId!.Value, new CancelYogaBookingRequest());   // SCRUM-107
        Assert.True(cancelled.IsSuccess, cancelled.ErrorMessage);
        Assert.Equal(YogaClassStatus.Open, (await db.YogaClasses.AsNoTracking().SingleAsync()).ClassStatus);
        Assert.NotNull((await db.YogaWaitlists.AsNoTracking().SingleAsync()).NotifiedAt);
        Assert.Equal("b3", (await db.Notifications.AsNoTracking().SingleAsync()).RecipientUserId);
        var seatTaken = await service.BookAsync("b3", new YogaBookingRequest(classId));
        Assert.True(seatTaken.IsSuccess, seatTaken.ErrorMessage);
        Assert.Equal(YogaWaitlistStatus.Promoted, (await db.YogaWaitlists.AsNoTracking().SingleAsync()).WaitlistStatus);

        var mine = (await service.GetMyBookingsAsync("b3")).Data!;   // SCRUM-108
        Assert.Equal(("Morning Flow", "Lotus"), (mine.Upcoming.Single().ClassName, mine.Upcoming.Single().StudioName));
        Assert.Empty(mine.Waitlist);
        Assert.Empty((await service.GetMyBookingsAsync("b1")).Data!.Upcoming);   // cancelled
    }

    [Fact]
    public async Task InstructorTakesAttendanceByQrAndRosterThenSessionCloses()   // SCRUM-110
    {
        await using var db = new SqliteTestContext(_options);
        var gym = await db.Gyms.FirstAsync();
        foreach (var id in new[] { "a1", "a2" })
            db.AddRange(new ApplicationUser { UserId = id, Gym = gym, Email = id + "@example.com", FullName = id, PhoneNumber = "090100003" + id[1], PasswordHash = "x", AccountStatus = AccountStatus.Active },
                TestSeed.Membership(id, ComponentType.YOGA));
        await db.SaveChangesAsync();
        var classId = await CreatePublishedClassAsync(db, max: 2);
        var bookings = new YogaBookingService(new YogaBookingRepository(db), TimeProvider.System);
        var noShow = (await bookings.BookAsync("a1", new YogaBookingRequest(classId))).Data!.BookingId!.Value;
        Assert.True((await bookings.BookAsync("a2", new YogaBookingRequest(classId))).IsSuccess);
        var session = await db.YogaClassSchedules.AsNoTracking().SingleAsync();
        var start = new DateTimeOffset(session.ScheduledDate.ToDateTime(session.StartTime), TimeSpan.FromHours(7));
        var checkIns = new CheckInRepository(db);
        var token = (await new MemberQrService(checkIns, TimeProvider.System).IssueAsync("a2")).Data!.Token;
        var attendance = new YogaAttendanceService(
            new YogaBookingRepository(db), new MemberQrService(checkIns, TimeProvider.System), new At(start.AddMinutes(40)));

        Assert.Equal(404, (await attendance.GetRosterAsync(session.Id, "someone-else", false)).StatusCode);
        var scan = await attendance.ScanQrAsync(session.Id, "yi-1", false, new YogaAttendanceQrRequest(token));
        Assert.True(scan.IsSuccess, scan.ErrorMessage);
        var submit = await attendance.SubmitAsync(session.Id, "yi-1", false,
            new SubmitYogaAttendanceRequest([new YogaAttendanceMark(noShow, false)]));
        Assert.True(submit.IsSuccess, submit.ErrorMessage);

        var rows = await db.YogaAttendances.AsNoTracking().OrderBy(a => a.MemberUserId).ToListAsync();
        Assert.Equal([("a1", YogaAttendanceStatus.NoShow), ("a2", YogaAttendanceStatus.Present)],
            rows.Select(r => (r.MemberUserId, r.AttendanceStatus)));
        Assert.True(await db.QRCodes.AnyAsync(q => q.IsActive && q.UserId == "a2"));   // the day-long code still works after the scan
        var listed = (await new YogaClassService(new YogaRepository(db), TimeProvider.System).GetClassSessionsAsync(classId)).Data!.Single();
        Assert.True(listed.AttendanceSubmittedLate);   // the Manager sees the late roster (GBR-86)
        Assert.Equal("ATTENDANCE_CLOSED", (await attendance.SubmitAsync(session.Id, "yi-1", false,
            new SubmitYogaAttendanceRequest([new YogaAttendanceMark(noShow, true)]))).ErrorCode);
    }

    /// <summary>
    /// TC-YEXP, option B (docs/BR-pending-yoga-plan-levels.md, GBR-Y13): when the yoga plan ends before the class does,
    /// the booking is held. The mat stays the member's, but they can't attend, aren't counted absent and aren't offered
    /// waitlist seats until they buy a new plan that covers the class.
    /// </summary>
    [Fact]
    public async Task ExpiredYogaPlanHoldsTheBookingUntilANewPlanIsBought()
    {
        await using var db = new SqliteTestContext(_options);
        var gym = await db.Gyms.FirstAsync();
        foreach (var id in new[] { "e1", "e2", "x1" })
            db.AddRange(new ApplicationUser { UserId = id, Gym = gym, Email = id + "@example.com", FullName = id, PhoneNumber = "090100004" + (id == "x1" ? '9' : id[1]), PasswordHash = "x", AccountStatus = AccountStatus.Active },
                TestSeed.Membership(id, ComponentType.YOGA, "Advanced"));
        await db.SaveChangesAsync();
        var classId = await CreatePublishedClassAsync(db, max: 2);   // runs from tomorrow to day 40, one session on day 10
        var bookings = new YogaBookingService(new YogaBookingRepository(db), TimeProvider.System);
        var booked = await bookings.BookAsync("e1", new YogaBookingRequest(classId));
        Assert.True(booked.IsSuccess, booked.ErrorMessage);
        Assert.True((await bookings.BookAsync("x1", new YogaBookingRequest(classId))).IsSuccess);
        Assert.Equal("Waitlisted", (await bookings.BookAsync("e2", new YogaBookingRequest(classId))).Data!.Status);

        // The plans of e1 and e2 end yesterday, the way the membership worker leaves them.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        async Task ExpirePlansAsync()
        {
            foreach (var plan in await db.MemberMemberships.Where(m => m.MemberUserId == "e1" || m.MemberUserId == "e2").ToListAsync())
                (plan.EndDate, plan.MembershipStatus) = (today.AddDays(-1), MembershipStatus.Expired);
            await db.SaveChangesAsync();
        }
        await ExpirePlansAsync();

        // TC-YEXP-01: the booking keeps its mat (still Confirmed) and "Sắp tới" shows it on hold, with the class level for "Mua gói mới".
        Assert.Equal(YogaBookingStatus.Confirmed, (await db.YogaBookings.AsNoTracking().SingleAsync(b => b.MemberUserId == "e1")).BookingStatus);
        var upcoming = Assert.Single((await bookings.GetMyBookingsAsync("e1")).Data!.Upcoming);
        Assert.True(upcoming.OnHold);
        Assert.Equal("Beginner", upcoming.DifficultyLevel);

        // TC-YEXP-03: a new booking needs a usable plan.
        await using (var other = new SqliteTestContext(_options))
            Assert.Equal("MEMBERSHIP_REQUIRED", (await new YogaBookingService(new YogaBookingRepository(other), TimeProvider.System)
                .BookAsync("e1", new YogaBookingRequest(classId))).ErrorCode);

        // TC-YEXP-04: a freed seat skips the waitlisted member whose plan ended.
        var x1Booking = (await db.YogaBookings.AsNoTracking().SingleAsync(b => b.MemberUserId == "x1")).Id;
        Assert.True((await bookings.CancelAsync("x1", x1Booking, new CancelYogaBookingRequest())).IsSuccess);
        Assert.Null((await db.YogaWaitlists.AsNoTracking().SingleAsync(w => w.MemberUserId == "e2")).NotifiedAt);

        // TC-YEXP-02/06: on the roster but held; the QR scan and a "Có mặt" mark are refused, and no mark is needed to submit.
        var session = await db.YogaClassSchedules.AsNoTracking().SingleAsync();
        var start = new DateTimeOffset(session.ScheduledDate.ToDateTime(session.StartTime), TimeSpan.FromHours(7));
        var checkIns = new CheckInRepository(db);
        var token = (await new MemberQrService(checkIns, TimeProvider.System).GetOrIssueAsync("e1")).Data!.Token;
        var attendance = new YogaAttendanceService(new YogaBookingRepository(db), new MemberQrService(checkIns, TimeProvider.System), new At(start.AddMinutes(5)));
        var row = Assert.Single((await attendance.GetRosterAsync(session.Id, "yi-1", false)).Data!.Roster);
        Assert.True(row.OnHold);
        Assert.Equal("YOGA_PLAN_ON_HOLD", (await attendance.ScanQrAsync(session.Id, "yi-1", false, new YogaAttendanceQrRequest(token))).ErrorCode);
        Assert.Equal("YOGA_PLAN_ON_HOLD", (await attendance.SubmitAsync(session.Id, "yi-1", false,
            new SubmitYogaAttendanceRequest([new YogaAttendanceMark(row.BookingId, true)]))).ErrorCode);
        var submitted = await attendance.SubmitAsync(session.Id, "yi-1", false, new SubmitYogaAttendanceRequest([]));
        Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
        Assert.False(await db.YogaAttendances.AnyAsync(a => a.MemberUserId == "e1"));   // not counted absent

        // TC-YEXP-05: buying a new plan that covers the class lifts the hold at once.
        db.Add(TestSeed.Membership("e1", ComponentType.YOGA, "Beginner"));
        await db.SaveChangesAsync();
        Assert.False(Assert.Single((await bookings.GetMyBookingsAsync("e1")).Data!.Upcoming).OnHold);

        // A late cancel of a held booking is not a no-show either (it could not have been attended).
        await ExpirePlansAsync();
        var late = new YogaBookingService(new YogaBookingRepository(db), new At(start.AddDays(7).AddHours(-1)));
        db.Add(new YogaClassSchedule
        {
            YogaClassId = classId, YogaStudioId = session.YogaStudioId, InstructorUserId = session.InstructorUserId,
            ScheduledDate = session.ScheduledDate.AddDays(7), StartTime = session.StartTime, EndTime = session.EndTime
        });
        await db.SaveChangesAsync();
        Assert.True((await late.CancelAsync("e1", row.BookingId, new CancelYogaBookingRequest(ConfirmLateCancellation: true))).IsSuccess);
        Assert.False(await db.YogaAttendances.AnyAsync(a => a.MemberUserId == "e1"));
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task WeeklySessionsFillChosenWeekdaysAndSaveNothingWhenOneDateClashes()
    {
        await using var db = new SqliteTestContext(_options);
        var repository = new YogaRepository(db);
        var studios = new YogaStudioService(repository, TimeProvider.System);
        var classes = new YogaClassService(repository, TimeProvider.System);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(10);
        while (start.DayOfWeek != DayOfWeek.Monday) start = start.AddDays(1);
        var studio = (await studios.CreateAsync(new YogaStudioRequest("Lotus", 2, 20), "manager", null)).Data!.Id;
        var type = (await classes.CreateClassTypeAsync(new YogaClassTypeRequest("Vinyasa", null), "manager", null)).Data!.Id;
        async Task<int> NewClass(string name) => (await classes.CreateClassAsync(new YogaClassRequest(
            type, name, "yi-1", "Beginner", 2, 1, 15, 60, DateTime.UtcNow.AddDays(5), start, start.AddDays(13)), "manager", null)).Data!.Id;
        var flow = await NewClass("Morning Flow");
        var other = await NewClass("Evening Flow");
        var weekly = new YogaWeeklySessionsRequest(studio, [DayOfWeek.Monday, DayOfWeek.Wednesday], new(9, 0));

        // The class has 2 sessions a week, so exactly 2 weekdays must be picked.
        DayOfWeek[][] wrongCounts = [[DayOfWeek.Monday], [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]];
        foreach (var days in wrongCounts)
            Assert.Equal("YOGA_SESSION_DAYS_COUNT", (await classes.CreateWeeklySessionsAsync(flow, weekly with { Days = days }, "manager", null)).ErrorCode);

        // Another class already holds the studio on the second Wednesday: nothing is saved, the message names the date.
        Assert.True((await classes.CreateSessionAsync(new YogaSessionRequest(other, studio, start.AddDays(9), new(9, 30), new(10, 30)), "manager", null)).IsSuccess);
        var clash = await classes.CreateWeeklySessionsAsync(flow, weekly, "manager", null);
        Assert.Equal("YOGA_SESSION_CONFLICT", clash.ErrorCode);
        Assert.StartsWith($"Ngày {start.AddDays(9):dd/MM/yyyy}", clash.ErrorMessage);
        Assert.False(await db.YogaClassSchedules.AnyAsync(s => s.YogaClassId == flow));

        var created = await classes.CreateWeeklySessionsAsync(flow, weekly with { StartTime = new(7, 0) }, "manager", null);
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal((4, 0), (created.Data!.Created, created.Data.Skipped));
        Assert.Equal([start, start.AddDays(2), start.AddDays(7), start.AddDays(9)],
            await db.YogaClassSchedules.Where(s => s.YogaClassId == flow).OrderBy(s => s.ScheduledDate).Select(s => s.ScheduledDate).ToListAsync());
        Assert.Equal("YOGA_NO_SESSION_DATES",   // a second run finds no free date
            (await classes.CreateWeeklySessionsAsync(flow, weekly with { StartTime = new(7, 0) }, "manager", null)).ErrorCode);
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
