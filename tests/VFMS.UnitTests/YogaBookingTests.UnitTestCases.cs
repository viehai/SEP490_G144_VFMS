using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

/// <summary>
/// Report 5.1 unit test cases: one test per UTCID column of the bookYogaClass, cancelYogaBooking and
/// submitYogaAttendance sheets. Names follow "Method_UTCIDnn_Condition".
/// </summary>
public sealed partial class YogaBookingTests
{
    // ── BookAsync (UC-38) ───────────────────────────────────────

    [Fact]
    public async Task BookAsync_UTCID01_ActivePlanWithoutMatChoiceGetsFirstFreeMat()
    {
        var (_, service, yogaClass) = Setup();

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((201, "Confirmed", 1), (result.StatusCode, result.Data!.Status, result.Data.MatNumber!.Value));
        Assert.Equal("Đặt lớp yoga thành công.", result.Data.Message);
        Assert.Equal(YogaClassStatus.Open, yogaClass.ClassStatus);
    }

    [Fact]
    public async Task BookAsync_UTCID02_ChosenMatEqualToClassSizeIsAccepted()
    {
        var (_, service, _) = Setup(max: 2);

        var result = await service.BookAsync("m1", new YogaBookingRequest(1, MatNumber: 2));

        Assert.Equal((201, 2), (result.StatusCode, result.Data!.MatNumber!.Value));
    }

    [Fact]
    public async Task BookAsync_UTCID03_LastSeatMakesTheClassFull()
    {
        var (_, service, yogaClass) = Setup(max: 2);
        await service.BookAsync("m1", new YogaBookingRequest(1));

        var result = await service.BookAsync("m2", new YogaBookingRequest(1));

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(YogaClassStatus.Full, yogaClass.ClassStatus);
    }

    [Fact]
    public async Task BookAsync_UTCID04_MatAboveClassSizeIsRejected()
    {
        var (_, service, yogaClass) = Setup(max: 2);

        var result = await service.BookAsync("m1", new YogaBookingRequest(1, MatNumber: 3));

        Assert.Equal((400, "INVALID_MAT"), (result.StatusCode, result.ErrorCode));
        Assert.Empty(yogaClass.Bookings);
    }

    [Fact]
    public async Task BookAsync_UTCID05_MatAlreadyTakenIsRejected()
    {
        var (_, service, _) = Setup();
        await service.BookAsync("m1", new YogaBookingRequest(1, MatNumber: 1));

        var result = await service.BookAsync("m2", new YogaBookingRequest(1, MatNumber: 1));

        Assert.Equal((409, "MAT_TAKEN"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID06_SecondBookingOfTheSameClassIsRejected()
    {
        var (_, service, _) = Setup();
        await service.BookAsync("m1", new YogaBookingRequest(1));

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((409, "ALREADY_BOOKED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID07_FullClassPutsMemberOnTheWaitlist()
    {
        var (repo, service, _) = Setup(max: 1);
        await service.BookAsync("m1", new YogaBookingRequest(1));

        var result = await service.BookAsync("m2", new YogaBookingRequest(1));

        Assert.Equal((202, "Waitlisted", 1), (result.StatusCode, result.Data!.Status, result.Data.WaitlistPosition!.Value));
        Assert.Equal("m2", Assert.Single(repo.Waitlist).MemberUserId);
    }

    [Fact]
    public async Task BookAsync_UTCID08_MemberAlreadyOnTheWaitlistIsToldTheirPosition()
    {
        var (repo, service, _) = Setup(max: 1);
        await service.BookAsync("m1", new YogaBookingRequest(1));
        await service.BookAsync("m2", new YogaBookingRequest(1));

        var result = await service.BookAsync("m2", new YogaBookingRequest(1));

        Assert.Equal((409, "ALREADY_WAITLISTED"), (result.StatusCode, result.ErrorCode));
        Assert.Single(repo.Waitlist);
    }

    [Fact]
    public async Task BookAsync_UTCID09_MemberWithoutAYogaPlanIsRejected()
    {
        var (repo, service, _) = Setup();
        repo.Memberships.Clear();
        repo.Memberships.Add(Membership("m1", ComponentType.GYM));

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((403, "MEMBERSHIP_REQUIRED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID10_PlanBelowTheClassLevelIsRejected()
    {
        var (repo, service, yogaClass) = Setup();
        yogaClass.DifficultyLevel = "Advanced";
        repo.Memberships.Clear();
        repo.Memberships.Add(Membership("m1", ComponentType.YOGA, "Beginner"));

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((403, "YOGA_LEVEL_NOT_COVERED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID11_ThirdNoShowWithin30DaysSuspendsBooking()
    {
        var (repo, service, _) = Setup();
        repo.NoShows.AddRange([Now.AddDays(-20), Now.AddDays(-10), Now.AddDays(-2)]);

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((403, "YOGA_BOOKING_SUSPENDED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID12_RegistrationClosesExactlyAtTheDeadline()
    {
        var (_, service, yogaClass) = Setup();
        yogaClass.RegistrationDeadline = Now;

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((409, "REGISTRATION_CLOSED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID13_DraftClassIsNotFound()
    {
        var (_, service, _) = Setup(status: YogaClassStatus.Draft);

        var result = await service.BookAsync("m1", new YogaBookingRequest(1));

        Assert.Equal((404, "YOGA_CLASS_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task BookAsync_UTCID14_UnknownClassIsNotFound()
    {
        var (_, service, _) = Setup();

        var result = await service.BookAsync("m1", new YogaBookingRequest(99));

        Assert.Equal((404, "YOGA_CLASS_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    // ── CancelAsync (UC-39) ─────────────────────────────────────

    /// <summary>Member m1 holds booking 5 (mat 1) in a full one-seat class; m2 waits. The next session starts after <paramref name="untilStart"/>.</summary>
    private static (Fake Repo, YogaBookingService Service, YogaClass Class) BookedIn(TimeSpan untilStart)
    {
        var (repo, service, yogaClass) = Booked(hoursToSession: 3);
        var start = Now.AddHours(7) + untilStart;   // gym-local
        yogaClass.Schedules.Single().ScheduledDate = DateOnly.FromDateTime(start);
        yogaClass.Schedules.Single().StartTime = TimeOnly.FromDateTime(start);
        return (repo, service, yogaClass);
    }

    [Fact]
    public async Task CancelAsync_UTCID01_EarlyCancelFreesTheSeatAndOffersItToTheWaitlist()
    {
        var (repo, service, yogaClass) = BookedIn(TimeSpan.FromHours(3));

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.Equal((200, "Cancelled"), (result.StatusCode, result.Data!.Status));
        Assert.Equal(YogaClassStatus.Open, yogaClass.ClassStatus);
        Assert.Empty(repo.Attendances);
        Assert.Equal("m2", Assert.Single(repo.Notifications).RecipientUserId);
    }

    [Fact]
    public async Task CancelAsync_UTCID02_ExactlyTwoHoursBeforeIsStillFree()
    {
        var (repo, service, _) = BookedIn(TimeSpan.FromHours(2));

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(repo.Attendances);
    }

    [Fact]
    public async Task CancelAsync_UTCID03_InsideTwoHoursNeedsConfirmation()
    {
        var (_, service, yogaClass) = BookedIn(TimeSpan.FromMinutes(119));

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.Equal((409, "LATE_CANCELLATION_CONFIRM"), (result.StatusCode, result.ErrorCode));
        Assert.Equal(YogaBookingStatus.Confirmed, yogaClass.Bookings.Single().BookingStatus);
    }

    [Fact]
    public async Task CancelAsync_UTCID04_ConfirmedLateCancelCountsAsNoShow()
    {
        var (repo, service, _) = BookedIn(TimeSpan.FromHours(1));

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest(ConfirmLateCancellation: true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(YogaAttendanceStatus.NoShow, Assert.Single(repo.Attendances).AttendanceStatus);
    }

    [Fact]
    public async Task CancelAsync_UTCID05_AnotherMembersBookingIsNotFound()
    {
        var (_, service, _) = BookedIn(TimeSpan.FromHours(3));

        var result = await service.CancelAsync("m2", 5, new CancelYogaBookingRequest());

        Assert.Equal((404, "YOGA_BOOKING_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task CancelAsync_UTCID06_AlreadyCancelledBookingIsNotFound()
    {
        var (_, service, yogaClass) = BookedIn(TimeSpan.FromHours(3));
        yogaClass.Bookings.Single().BookingStatus = YogaBookingStatus.Cancelled;

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.Equal((404, "YOGA_BOOKING_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task CancelAsync_UTCID07_UnknownBookingIsNotFound()
    {
        var (_, service, _) = BookedIn(TimeSpan.FromHours(3));

        var result = await service.CancelAsync("m1", 999, new CancelYogaBookingRequest());

        Assert.Equal((404, "YOGA_BOOKING_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task CancelAsync_UTCID08_NoUpcomingSessionLeftToCancel()
    {
        var (_, service, _) = BookedIn(TimeSpan.FromHours(-1));

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.Equal((409, "NO_UPCOMING_SESSION"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task CancelAsync_UTCID09_ThirdLateCancelWarnsAboutTheSuspension()
    {
        var (repo, service, _) = BookedIn(TimeSpan.FromHours(1));
        repo.NoShows.AddRange([Now.AddDays(-12), Now.AddDays(-4)]);

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest(ConfirmLateCancellation: true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("bị khóa đặt lớp yoga đến 14/11/2026 10:00", result.Data!.Message);
    }

    [Fact]
    public async Task CancelAsync_UTCID10_WaitingMemberWithoutACoveringPlanIsSkipped()
    {
        var (repo, service, _) = BookedIn(TimeSpan.FromHours(3));
        repo.Memberships.RemoveAll(m => m.MemberUserId == "m2");

        var result = await service.CancelAsync("m1", 5, new CancelYogaBookingRequest());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(repo.Notifications);
        Assert.Null(repo.Waitlist.Single().NotifiedAt);
    }

    // ── YogaAttendanceService.SubmitAsync (UC-42) ───────────────

    /// <summary>Session 20 taught by "yi" started <paramref name="sinceStart"/> ago (negative: starts later); m1 and m2 are booked.</summary>
    private static (Fake Repo, YogaAttendanceService Service, YogaClassSchedule Session) RosterSession(TimeSpan sinceStart)
    {
        var (repo, _, yogaClass) = Setup();
        yogaClass.InstructorUserId = "yi";
        var start = Now.AddHours(7) - sinceStart;   // gym-local
        var session = new YogaClassSchedule
        {
            Id = 20, YogaClass = yogaClass, YogaClassId = 1, ScheduledDate = DateOnly.FromDateTime(start),
            StartTime = TimeOnly.FromDateTime(start), EndTime = TimeOnly.FromDateTime(start).AddHours(1)
        };
        yogaClass.Schedules.Add(session);
        foreach (var (id, member) in new[] { (1, "m1"), (2, "m2") })
            yogaClass.Bookings.Add(new YogaBooking
            {
                Id = id, YogaClassId = 1, MemberUserId = member, MatNumber = id, BookedAt = Now.AddDays(-5),
                BookingStatus = YogaBookingStatus.Confirmed, Member = new ApplicationUser { UserId = member, FullName = member }
            });
        return (repo, new YogaAttendanceService(repo, new QrFake(), new FixedClock(NowOffset)), session);
    }

    private static SubmitYogaAttendanceRequest Marks(params (int Booking, bool Attended)[] marks) =>
        new(marks.Select(m => new YogaAttendanceMark(m.Booking, m.Attended)).ToList());

    [Fact]
    public async Task SubmitAttendance_UTCID01_InstructorMarksEveryoneOnTime()
    {
        var (_, service, session) = RosterSession(TimeSpan.FromMinutes(10));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, false)));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new string?[] { "Attended", "No-show" }, result.Data!.Roster.Select(r => r.Mark));
        Assert.False(result.Data.SubmittedLate);
        Assert.Equal(Now, session.AttendanceSubmittedAt);
    }

    [Fact]
    public async Task SubmitAttendance_UTCID02_ExactlyThirtyMinutesAfterStartIsNotLate()
    {
        var (_, service, _) = RosterSession(TimeSpan.FromMinutes(30));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.False(result.Data!.SubmittedLate);
    }

    [Fact]
    public async Task SubmitAttendance_UTCID03_ThirtyOneMinutesAfterStartIsFlaggedLate()
    {
        var (_, service, _) = RosterSession(TimeSpan.FromMinutes(31));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.True(result.Data!.SubmittedLate);
    }

    [Fact]
    public async Task SubmitAttendance_UTCID04_StaffSubmitRecordsTheSessionInstructor()
    {
        var (_, service, session) = RosterSession(TimeSpan.FromMinutes(10));

        var result = await service.SubmitAsync(20, "staff-1", true, Marks((1, true), (2, false)));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.All(session.Attendances, a => Assert.Equal("yi", a.InstructorUserId));
    }

    [Fact]
    public async Task SubmitAttendance_UTCID05_AnotherInstructorCannotOpenTheSession()
    {
        var (_, service, _) = RosterSession(TimeSpan.FromMinutes(10));

        var result = await service.SubmitAsync(20, "yi-other", false, Marks((1, true), (2, false)));

        Assert.Equal((404, "YOGA_SESSION_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SubmitAttendance_UTCID06_MissingMarkIsRejected()
    {
        var (_, service, session) = RosterSession(TimeSpan.FromMinutes(10));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true)));

        Assert.Equal((400, "ROSTER_INCOMPLETE"), (result.StatusCode, result.ErrorCode));
        Assert.Null(session.AttendanceSubmittedAt);
    }

    [Fact]
    public async Task SubmitAttendance_UTCID07_MarkForABookingOffTheRosterIsRejected()
    {
        var (_, service, _) = RosterSession(TimeSpan.FromMinutes(10));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true), (9, true)));

        Assert.Equal((400, "NOT_ON_ROSTER"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SubmitAttendance_UTCID08_SubmittedSessionIsClosed()
    {
        var (_, service, session) = RosterSession(TimeSpan.FromMinutes(10));
        session.AttendanceSubmittedAt = Now.AddMinutes(-1);

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.Equal((409, "ATTENDANCE_CLOSED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SubmitAttendance_UTCID09_ThirtyOneMinutesBeforeStartIsTooEarly()
    {
        var (_, service, _) = RosterSession(TimeSpan.FromMinutes(-31));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.Equal((409, "SESSION_NOT_STARTED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SubmitAttendance_UTCID10_ThirtyMinutesBeforeStartIsAllowed()
    {
        var (_, service, _) = RosterSession(TimeSpan.FromMinutes(-30));

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAttendance_UTCID11_CancelledSessionIsNotFound()
    {
        var (_, service, session) = RosterSession(TimeSpan.FromMinutes(10));
        session.ScheduleStatus = YogaScheduleStatus.Cancelled;

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.Equal((404, "YOGA_SESSION_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task SubmitAttendance_UTCID12_MemberWithAnExpiredPlanCannotBeMarkedPresent()
    {
        var (repo, service, _) = RosterSession(TimeSpan.FromMinutes(10));
        repo.Memberships.Single(m => m.MemberUserId == "m2").EndDate = Today.AddDays(-1);

        var result = await service.SubmitAsync(20, "yi", false, Marks((1, true), (2, true)));

        Assert.Equal((403, "YOGA_PLAN_ON_HOLD"), (result.StatusCode, result.ErrorCode));
    }
}
