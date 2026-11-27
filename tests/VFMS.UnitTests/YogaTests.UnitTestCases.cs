using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

/// <summary>
/// Report 5.1 unit test cases for the createYogaSession and publishYogaClass sheets, one test per UTCID.
/// Today is 02/11/2026 10:00 gym time; class 7 (pt-1, 60 minutes, max 15) runs 02/11–02/12 in studio 1 (20 mats).
/// </summary>
public sealed partial class YogaTests
{
    private static YogaSessionRequest SessionAt(int classId, int studioId, int days, TimeOnly start, TimeOnly end, string? instructor = null) =>
        new(classId, studioId, Today.AddDays(days), start, end, instructor);

    // ── YogaClassService.CreateSessionAsync (UC-34) ─────────────

    [Fact]
    public async Task CreateSession_UTCID01_ValidSessionIsSaved()
    {
        var repo = Seeded(out var yogaClass, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(Today.AddDays(10), Assert.Single(repo.Sessions).ScheduledDate);
    }

    [Fact]
    public async Task CreateSession_UTCID02_UnknownClassIsNotFound()
    {
        var repo = Seeded(out _, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(999, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal(404, result.StatusCode);
        Assert.Empty(repo.Sessions);
    }

    [Fact]
    public async Task CreateSession_UTCID03_CancelledClassTakesNoSessions()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Cancelled;

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal((409, "YOGA_CLASS_CLOSED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task CreateSession_UTCID04_SessionOnTheClassEndDateIsAccepted()
    {
        var repo = Seeded(out var yogaClass, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 30, new(9, 0), new(10, 0)), "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task CreateSession_UTCID05_DayAfterTheClassEndDateIsRejected()
    {
        var repo = Seeded(out var yogaClass, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 31, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_SESSION_OUTSIDE_CLASS", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID06_SessionStartingInThePastIsRejected()
    {
        var repo = Seeded(out var yogaClass, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 0, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_SESSION_IN_PAST", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID07_LengthDifferentFromTheClassDurationIsRejected()
    {
        var repo = Seeded(out var yogaClass, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 30)), "manager", null);

        Assert.Equal("YOGA_SESSION_DURATION", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID08_InactiveStudioIsRejected()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        studio.IsActive = false;

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_STUDIO_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID09_StudioOneMatSmallerThanTheClassIsRejected()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        studio.Capacity = yogaClass.MaxParticipants - 1;

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_STUDIO_TOO_SMALL", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID10_StudioExactlyTheClassSizeIsAccepted()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        studio.Capacity = yogaClass.MaxParticipants;

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task CreateSession_UTCID11_SubstituteWhoIsNotAYogaInstructorIsRejected()
    {
        var repo = Seeded(out var yogaClass, out var studio);

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0), instructor: "receptionist"), "manager", null);

        Assert.Equal("YOGA_INVALID_INSTRUCTOR", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID12_FifteenMinuteGapInTheSameStudioIsAccepted()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        repo.Sessions.Add(Session(50, studio, Class("other"), Today.AddDays(10), new(7, 45), new(8, 45)));

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task CreateSession_UTCID13_FourteenMinuteGapInTheSameStudioIsAConflict()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        repo.Sessions.Add(Session(50, studio, Class("other"), Today.AddDays(10), new(7, 46), new(8, 46)));

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal((409, "YOGA_SESSION_CONFLICT"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task CreateSession_UTCID14_PublishedClassNeedsSevenDaysForANewSession()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Open;

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 6, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_PUBLISH_TOO_LATE", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSession_UTCID15_SameInstructorBusyInAnotherStudioIsAConflict()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        var otherStudio = new YogaStudio { Id = 2, GymId = 1, StudioName = "Sen", FloorNumber = 3, Capacity = 20, IsActive = true };
        repo.Studios.Add(otherStudio);
        repo.Sessions.Add(Session(50, otherStudio, Class("pt-1"), Today.AddDays(10), new(9, 30), new(10, 30)));

        var result = await new YogaClassService(repo, Clock).CreateSessionAsync(
            SessionAt(yogaClass.Id, studio.Id, 10, new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal((409, "YOGA_SESSION_CONFLICT"), (result.StatusCode, result.ErrorCode));
    }

    // ── YogaClassService.PublishClassAsync (UC-34) ──────────────

    private static YogaRepositoryFake DraftWithSessionIn(int days, out YogaClass yogaClass)
    {
        var repo = Seeded(out yogaClass, out var studio);
        yogaClass.Schedules.Add(Session(10, studio, yogaClass, Today.AddDays(days), new(9, 0), new(10, 0)));
        return repo;
    }

    [Fact]
    public async Task PublishClass_UTCID01_DraftWithAFutureSessionOpens()
    {
        var repo = DraftWithSessionIn(10, out var yogaClass);

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(YogaClassStatus.Open, yogaClass.ClassStatus);
    }

    [Fact]
    public async Task PublishClass_UTCID02_FirstSessionExactlySevenDaysAwayIsAccepted()
    {
        var repo = DraftWithSessionIn(7, out var yogaClass);

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task PublishClass_UTCID03_FirstSessionSixDaysAwayIsTooLate()
    {
        var repo = DraftWithSessionIn(6, out var yogaClass);

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal("YOGA_PUBLISH_TOO_LATE", result.ErrorCode);
        Assert.Equal(YogaClassStatus.Draft, yogaClass.ClassStatus);
    }

    [Fact]
    public async Task PublishClass_UTCID04_UnknownClassIsNotFound()
    {
        var repo = DraftWithSessionIn(10, out _);

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(999, "manager", null);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task PublishClass_UTCID05_AlreadyOpenClassIsNotADraft()
    {
        var repo = DraftWithSessionIn(10, out var yogaClass);
        yogaClass.ClassStatus = YogaClassStatus.Open;

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal((409, "YOGA_CLASS_NOT_DRAFT"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task PublishClass_UTCID06_ClassWithoutSessionsIsRejected()
    {
        var repo = Seeded(out var yogaClass, out _);

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal("YOGA_CLASS_NO_SESSIONS", result.ErrorCode);
    }

    [Fact]
    public async Task PublishClass_UTCID07_OnlyCancelledSessionsCountAsNone()
    {
        var repo = DraftWithSessionIn(10, out var yogaClass);
        yogaClass.Schedules.Single().ScheduleStatus = YogaScheduleStatus.Cancelled;

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal("YOGA_CLASS_NO_SESSIONS", result.ErrorCode);
    }

    [Fact]
    public async Task PublishClass_UTCID08_DeadlineEqualToNowHasPassed()
    {
        var repo = DraftWithSessionIn(10, out var yogaClass);
        yogaClass.RegistrationDeadline = Clock.GetUtcNow().UtcDateTime;

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal("YOGA_DEADLINE_PASSED", result.ErrorCode);
    }

    [Fact]
    public async Task PublishClass_UTCID09_InstructorNoLongerActiveIsRejected()
    {
        var repo = DraftWithSessionIn(10, out var yogaClass);
        repo.Roles.Remove("pt-1");

        var result = await new YogaClassService(repo, Clock).PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal("YOGA_INSTRUCTOR_INACTIVE", result.ErrorCode);
        Assert.Equal(YogaClassStatus.Draft, yogaClass.ClassStatus);
    }
}
