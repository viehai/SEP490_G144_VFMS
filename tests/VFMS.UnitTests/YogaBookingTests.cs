using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class YogaBookingTests
{
    // 03:00 UTC = 10:00 gym time on 7 Nov 2026.
    private static readonly DateTimeOffset NowOffset = new(2026, 11, 7, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Now = NowOffset.UtcDateTime;
    private static readonly DateOnly Today = new(2026, 11, 7);

    private static (Fake Repo, YogaBookingService Service, YogaClass Class) Setup(int max = 2, YogaClassStatus status = YogaClassStatus.Open)
    {
        var repo = new Fake();
        var yogaClass = new YogaClass
        {
            Id = 1, ClassName = "Morning Flow", YogaClassTypeId = 4, MaxParticipants = max, ClassStatus = status,
            RegistrationDeadline = Now.AddDays(2)
        };
        yogaClass.Schedules.Add(new YogaClassSchedule
        {
            Id = 11, YogaClassId = 1, ScheduledDate = Today.AddDays(3), StartTime = new(9, 0), EndTime = new(10, 0)
        });
        repo.Class = yogaClass;
        foreach (var member in new[] { "m1", "m2", "m3" })
            repo.Memberships.Add(Membership(member));
        return (repo, new YogaBookingService(repo, new FixedClock(NowOffset)), yogaClass);
    }

    private static MemberMembership Membership(string user, ComponentType type = ComponentType.YOGA, int? classTypeId = null) => new()
    {
        MemberUserId = user, MembershipStatus = MembershipStatus.Active, StartDate = Today.AddDays(-5), EndDate = Today.AddDays(25),
        PackageComponent = new PackageComponent { ComponentType = type, YogaClassTypeId = classTypeId }
    };

    [Fact]
    public async Task BookingTakesAMatAndTheLastSeatMakesTheClassFull()   // GBR-79, GBR-80, MSG-32, MSG-33
    {
        var (repo, service, yogaClass) = Setup();

        var first = await service.BookAsync("m1", new YogaBookingRequest(1));
        var taken = await service.BookAsync("m2", new YogaBookingRequest(1, MatNumber: 1));
        var twice = await service.BookAsync("m1", new YogaBookingRequest(1));
        var outside = await service.BookAsync("m2", new YogaBookingRequest(1, MatNumber: 9));
        var last = await service.BookAsync("m2", new YogaBookingRequest(1, MatNumber: 2));

        Assert.Equal((201, 1, "Confirmed"), (first.StatusCode, first.Data!.MatNumber!.Value, first.Data.Status));
        Assert.Equal("MAT_TAKEN", taken.ErrorCode);
        Assert.Equal("ALREADY_BOOKED", twice.ErrorCode);
        Assert.Equal("INVALID_MAT", outside.ErrorCode);
        Assert.True(last.IsSuccess);
        Assert.Equal(YogaClassStatus.Full, yogaClass.ClassStatus);
        Assert.Equal(5, repo.LockCount);   // every booking attempt ran under the class lock
        Assert.Equal([1, 2], (await service.GetMatMapAsync(1)).Data!.TakenMats);
    }

    [Fact]
    public async Task FullClassQueuesMemberAndWaitlistedMemberTakesAFreedSeat()   // GBR-80, GBR-82
    {
        var (repo, service, yogaClass) = Setup(max: 1);
        await service.BookAsync("m1", new YogaBookingRequest(1));

        var queued = await service.BookAsync("m2", new YogaBookingRequest(1));
        var queuedAgain = await service.BookAsync("m2", new YogaBookingRequest(1));

        Assert.Equal((202, "Waitlisted", 1), (queued.StatusCode, queued.Data!.Status, queued.Data.WaitlistPosition!.Value));
        var entry = Assert.Single(repo.Waitlist);
        Assert.Equal(11, entry.YogaClassScheduleId);   // attached to the next session
        Assert.Equal("ALREADY_WAITLISTED", queuedAgain.ErrorCode);

        yogaClass.Bookings.Single().BookingStatus = YogaBookingStatus.Cancelled;   // a seat opens
        var promoted = await service.BookAsync("m2", new YogaBookingRequest(1));

        Assert.Equal(201, promoted.StatusCode);
        Assert.Equal(YogaWaitlistStatus.Promoted, entry.WaitlistStatus);
    }

    [Theory]
    [InlineData(ComponentType.GYM, null)]   // gym-only package
    [InlineData(ComponentType.YOGA, 99)]    // yoga package for another class type
    public async Task BookingNeedsAnActiveYogaPackageForThisClassType(ComponentType type, int? classTypeId)   // MSG-31
    {
        var (repo, service, _) = Setup();
        repo.Memberships.Clear();
        repo.Memberships.Add(Membership("m1", type, classTypeId));

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((403, "MEMBERSHIP_REQUIRED"), (result.StatusCode, result.ErrorCode));
        repo.Memberships.Add(Membership("m1", ComponentType.YOGA, 4));
        Assert.True((await service.BookAsync("m1", new YogaBookingRequest(1))).IsSuccess);
    }

    [Fact]
    public async Task RegistrationMustBeOpenAndBeforeTheDeadline()   // GBR-78
    {
        var (_, service, yogaClass) = Setup();
        yogaClass.RegistrationDeadline = Now.AddMinutes(-1);
        Assert.Equal("REGISTRATION_CLOSED", (await service.BookAsync("m1", new YogaBookingRequest(1))).ErrorCode);

        yogaClass.RegistrationDeadline = Now.AddDays(1);
        yogaClass.ClassStatus = YogaClassStatus.Cancelled;
        Assert.Equal("REGISTRATION_CLOSED", (await service.BookAsync("m1", new YogaBookingRequest(1))).ErrorCode);

        yogaClass.ClassStatus = YogaClassStatus.Draft;
        Assert.Equal(404, (await service.BookAsync("m1", new YogaBookingRequest(1))).StatusCode);
    }

    [Fact]
    public async Task ThirdNoShowIn30DaysBlocksBookingFor7Days()   // GBR-84
    {
        var (repo, service, _) = Setup();
        repo.NoShows.AddRange([Now.AddDays(-20), Now.AddDays(-10), Now.AddDays(-2)]);

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((403, "YOGA_BOOKING_SUSPENDED"), (result.StatusCode, result.ErrorCode));
        Assert.Contains("12/11/2026 10:00", result.ErrorMessage);   // 2 days ago + 7 days, gym time
    }

    [Theory]
    [InlineData(new[] { -20, -10, -2 }, 5)]       // 3rd no-show 2 days ago -> 5 days left
    [InlineData(new[] { -20, -10, -8 }, null)]    // ban already over
    [InlineData(new[] { -40, -10, -2 }, null)]    // only 2 within 30 days
    [InlineData(new[] { -2 }, null)]
    public void SuspensionEndsSevenDaysAfterTheThirdNoShow(int[] daysAgo, int? daysLeft)
    {
        var until = YogaBookingService.SuspendedUntil(daysAgo.Select(d => Now.AddDays(d)).ToList(), Now);
        Assert.Equal(daysLeft is null ? (DateTime?)null : Now.AddDays(daysLeft.Value), until);
    }

    // ── UC-39 cancel ─────────────────────────────────────────────

    private static (Fake Repo, YogaBookingService Service, YogaClass Class) Booked(int hoursToSession)
    {
        var (repo, _, yogaClass) = Setup(max: 1, status: YogaClassStatus.Full);
        var start = Now.AddHours(7 + hoursToSession);   // gym-local
        yogaClass.Schedules.Single().ScheduledDate = DateOnly.FromDateTime(start);
        yogaClass.Schedules.Single().StartTime = TimeOnly.FromDateTime(start);
        yogaClass.InstructorUserId = "yi-1";
        yogaClass.Bookings.Add(new YogaBooking { Id = 5, YogaClassId = 1, MemberUserId = "m1", MatNumber = 1, BookingStatus = YogaBookingStatus.Confirmed });
        repo.Waitlist.Add(new YogaWaitlist { MemberUserId = "m2", Position = 1, YogaClassScheduleId = 11 });
        return (repo, new YogaBookingService(repo, new FixedClock(NowOffset)), yogaClass);
    }

    [Fact]
    public async Task CancellingEarlyFreesTheSeatWithoutPenaltyAndNotifiesTheWaitlist()   // GBR-82, GBR-83
    {
        var (repo, service, yogaClass) = Booked(hoursToSession: 3);

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.Equal(("Cancelled", "Yoga booking updated successfully."), (result.Data!.Status, result.Data.Message));
        Assert.Equal(YogaBookingStatus.Cancelled, yogaClass.Bookings.Single().BookingStatus);
        Assert.Equal(YogaClassStatus.Open, yogaClass.ClassStatus);
        Assert.Empty(repo.Attendances);
        Assert.Equal("m2", Assert.Single(repo.Notifications).RecipientUserId);
        Assert.Equal(Now, repo.Waitlist.Single().NotifiedAt);
        Assert.Equal(404, (await service.CancelAsync("m1", 5, new CancelYogaBookingRequest())).StatusCode);   // already cancelled
    }

    [Fact]
    public async Task LateCancellationNeedsConfirmationAndCountsAsNoShow()   // GBR-83, MSG-34
    {
        var (repo, service, yogaClass) = Booked(hoursToSession: 1);

        var asked = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());
        Assert.Equal((409, "LATE_CANCELLATION_CONFIRM"), (asked.StatusCode, asked.ErrorCode));
        Assert.Equal(YogaBookingStatus.Confirmed, yogaClass.Bookings.Single().BookingStatus);

        var confirmed = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest(ConfirmLateCancellation: true));
        Assert.True(confirmed.IsSuccess);
        var noShow = Assert.Single(repo.Attendances);
        Assert.Equal((11, YogaAttendanceStatus.NoShow, "yi-1"), (noShow.YogaClassScheduleId, noShow.AttendanceStatus, noShow.InstructorUserId));
    }

    [Fact]
    public async Task LateCancelAfterAttendanceWasMarkedAddsNoSecondRow()   // UC-39 x UC-42
    {
        var (repo, service, yogaClass) = Booked(hoursToSession: 1);
        yogaClass.Schedules.Single().Attendances.Add(new YogaAttendance { YogaBookingId = 5, AttendanceStatus = YogaAttendanceStatus.Present });

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest(ConfirmLateCancellation: true));

        Assert.True(result.IsSuccess);
        Assert.Empty(repo.Attendances);   // the Present mark stays the only row for that session
    }

    [Fact]
    public async Task ThirdLateCancellationTellsTheMemberAboutTheSuspension()   // SRS 7.6 step 3a
    {
        var (repo, service, _) = Booked(hoursToSession: 1);
        repo.NoShows.AddRange([Now.AddDays(-12), Now.AddDays(-4)]);

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest(ConfirmLateCancellation: true));

        Assert.Contains("suspended until 14/11/2026 10:00", result.Data!.Message);
    }

    [Fact]
    public async Task MemberCannotCancelSomeoneElsesBooking()
    {
        var (_, service, _) = Booked(hoursToSession: 3);
        Assert.Equal(404, (await service.CancelAsync("m2", 5, new CancelYogaBookingRequest())).StatusCode);
    }

    // ── UC-40 my bookings ────────────────────────────────────────

    [Fact]
    public async Task MyBookingsShowNextSessionPastAttendanceAndWaitlist()
    {
        var (repo, service, yogaClass) = Setup();
        var studio = new YogaStudio { StudioName = "Lotus" };
        yogaClass.Schedules.Clear();
        YogaClassSchedule Session(int id, int days, YogaScheduleStatus status = YogaScheduleStatus.Scheduled) => new()
        {
            Id = id, YogaClass = yogaClass, YogaClassId = 1, YogaStudio = studio, ScheduledDate = Today.AddDays(days),
            StartTime = new(9, 0), EndTime = new(10, 0), ScheduleStatus = status
        };
        foreach (var s in new[] { Session(1, -8), Session(2, -1), Session(3, -2, YogaScheduleStatus.Cancelled), Session(4, 6), Session(5, 3) })
            yogaClass.Schedules.Add(s);
        var booking = new YogaBooking
        {
            Id = 7, YogaClass = yogaClass, YogaClassId = 1, MemberUserId = "m1", MatNumber = 3, BookedAt = Now.AddDays(-10),
            BookingStatus = YogaBookingStatus.Confirmed
        };
        booking.Attendances.Add(new YogaAttendance { YogaClassScheduleId = 1, AttendanceStatus = YogaAttendanceStatus.Present });
        yogaClass.Bookings.Add(booking);
        repo.Waitlist.Add(new YogaWaitlist { MemberUserId = "m1", Position = 2, NotifiedAt = Now,
            YogaClassSchedule = new YogaClassSchedule { YogaClassId = 9, YogaClass = new YogaClass { ClassName = "Evening Yin" } } });

        var mine = (await service.GetMyBookingsAsync("m1")).Data!;

        var next = Assert.Single(mine.Upcoming);
        Assert.Equal((Today.AddDays(3), "Lotus", 3), (next.NextDate, next.StudioName, next.MatNumber!.Value));
        Assert.Equal([(2, "Not recorded"), (1, "Attended")], mine.Past.Select(p => (p.SessionId, p.Result)));   // newest first, cancelled session skipped
        Assert.Equal(("Evening Yin", 2, true), (mine.Waitlist.Single().ClassName, mine.Waitlist.Single().Position, mine.Waitlist.Single().SeatOffered));
        Assert.Empty((await service.GetMyBookingsAsync("m2")).Data!.Upcoming);
    }

    // ── UC-42 attendance ─────────────────────────────────────────

    [Fact]
    public async Task InstructorTakesAttendanceQrCountsOnlyForBookedMembersAndLateSubmitIsFlagged()   // GBR-86, GBR-87, GBR-88
    {
        var (repo, _, yogaClass) = Setup();
        yogaClass.InstructorUserId = "yi";
        yogaClass.Schedules.Single().YogaClass = yogaClass;   // session 11, in 3 days
        var session = new YogaClassSchedule
        {
            Id = 20, YogaClass = yogaClass, YogaClassId = 1, ScheduledDate = Today, StartTime = new(9, 0), EndTime = new(10, 0)
        };   // started 60 minutes ago
        yogaClass.Schedules.Add(session);
        YogaBooking Book(int id, string member, DateTime bookedAt, DateTime? cancelledAt = null) => new()
        {
            Id = id, YogaClassId = 1, MemberUserId = member, MatNumber = id, BookedAt = bookedAt, CancelledAt = cancelledAt,
            BookingStatus = cancelledAt is null ? YogaBookingStatus.Confirmed : YogaBookingStatus.Cancelled,
            Member = new ApplicationUser { UserId = member, FullName = member }
        };
        yogaClass.Bookings.Add(Book(1, "m1", Now.AddDays(-5)));
        yogaClass.Bookings.Add(Book(2, "m2", Now.AddDays(-5)));
        yogaClass.Bookings.Add(Book(3, "m3", Now));                                  // enrolled after the session started
        yogaClass.Bookings.Add(Book(4, "m4", Now.AddDays(-5), Now.AddHours(-3)));   // cancelled before it
        var service = new YogaAttendanceService(repo, new QrFake { ["t-m2"] = "m2", ["t-m3"] = "m3" }, new FixedClock(NowOffset));

        Assert.Equal(404, (await service.GetRosterAsync(20, "another-instructor", false)).StatusCode);
        Assert.Equal(["m1", "m2"], (await service.GetRosterAsync(20, "yi", false)).Data!.Roster.Select(r => r.MemberUserId));

        var notBooked = await service.ScanQrAsync(20, "yi", false, new YogaAttendanceQrRequest("t-m3"));
        Assert.Equal((403, "ACCESS_DENIED"), (notBooked.StatusCode, notBooked.ErrorCode));   // MSG-36
        var scanned = await service.ScanQrAsync(20, "yi", false, new YogaAttendanceQrRequest("t-m2"));
        Assert.Equal(new string?[] { null, "Attended" }, scanned.Data!.Roster.Select(r => r.Mark));

        Assert.Equal("ROSTER_INCOMPLETE", (await service.SubmitAsync(20, "yi", false, new SubmitYogaAttendanceRequest([]))).ErrorCode);
        Assert.Equal("NOT_ON_ROSTER", (await service.SubmitAsync(20, "yi", false,
            new SubmitYogaAttendanceRequest([new(1, false), new(3, true)]))).ErrorCode);
        var submitted = await service.SubmitAsync(20, "staff", true, new SubmitYogaAttendanceRequest([new(1, false)]));

        Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
        Assert.True(submitted.Data!.SubmittedLate);   // 60 minutes after start
        Assert.Equal(new string?[] { "No-show", "Attended" }, submitted.Data.Roster.Select(r => r.Mark));
        Assert.All(session.Attendances, a => Assert.Equal("yi", a.InstructorUserId));   // the session's instructor, not the Staff user
        Assert.Equal(YogaBookingStatus.Confirmed, yogaClass.Bookings.First().BookingStatus);
        Assert.Equal("ATTENDANCE_CLOSED", (await service.ScanQrAsync(20, "yi", false, new YogaAttendanceQrRequest("t-m2"))).ErrorCode);
        Assert.Equal("SESSION_NOT_STARTED", (await service.SubmitAsync(11, "yi", false, new SubmitYogaAttendanceRequest([]))).ErrorCode);
    }

    private sealed class QrFake : Dictionary<string, string>, IMemberQrService
    {
        public Task<VFMS.Contracts.Common.Result<VFMS.Contracts.CheckIn.MemberQrResponse>> IssueAsync(
            string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> RedeemAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(TryGetValue(token, out var user) ? user : null);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Fake : IYogaBookingRepository
    {
        public YogaClass? Class { get; set; }
        public List<MemberMembership> Memberships { get; } = [];
        public List<DateTime> NoShows { get; } = [];
        public List<YogaWaitlist> Waitlist { get; } = [];
        public int LockCount { get; private set; }

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity is YogaBooking booking) { booking.Id = Class!.Bookings.Count + 1; Class.Bookings.Add(booking); }
            if (entity is YogaWaitlist entry) Waitlist.Add(entry);
            if (entity is YogaAttendance attendance) { Attendances.Add(attendance); NoShows.Add(attendance.RecordedAt); }
            if (entity is Notification notification) Notifications.Add(notification);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<YogaClass?> GetClassAsync(int classId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Class?.Id == classId ? Class : null);
        public Task LockClassAsync(int classId, CancellationToken cancellationToken = default) { LockCount++; return Task.CompletedTask; }
        public Task<IReadOnlyList<MemberMembership>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemberMembership>>(Memberships.Where(m => m.MemberUserId == userId).ToList());
        public Task<IReadOnlyList<DateTime>> GetNoShowTimesAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DateTime>>(NoShows.Where(t => t >= sinceUtc).ToList());
        public List<YogaAttendance> Attendances { get; } = [];
        public List<Notification> Notifications { get; } = [];
        public Task<YogaClassSchedule?> GetSessionAsync(int sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Class?.Schedules.FirstOrDefault(s => s.Id == sessionId));
        public Task LockSessionAsync(int sessionId, CancellationToken cancellationToken = default) { LockCount++; return Task.CompletedTask; }
        public Task<IReadOnlyList<YogaBooking>> GetMemberBookingsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaBooking>>(Class!.Bookings.Where(b => b.MemberUserId == userId).ToList());
        public Task<IReadOnlyList<YogaWaitlist>> GetMemberWaitlistAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaWaitlist>>(Waitlist.Where(w => w.MemberUserId == userId && w.WaitlistStatus == YogaWaitlistStatus.Waiting).ToList());
        public Task<YogaBooking?> GetBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Class?.Bookings.FirstOrDefault(b => b.Id == bookingId));
        public Task<IReadOnlyList<YogaWaitlist>> GetWaitlistAsync(int classId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaWaitlist>>(Waitlist.Where(w => w.WaitlistStatus == YogaWaitlistStatus.Waiting).OrderBy(w => w.Position).ToList());
    }
}
