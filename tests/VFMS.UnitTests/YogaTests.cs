using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Contracts.Yoga.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class YogaTests
{
    // 2026-11-02 03:00 UTC = 10:00 Monday at the gym (UTC+7).
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 11, 2);

    // ── GBR-71 / GBR-72 studios ─────────────────────────────────

    [Fact]
    public async Task StudioNameMustBeUniqueInGym()
    {
        var repo = new YogaRepositoryFake();
        repo.Studios.Add(new YogaStudio { Id = 1, GymId = 1, StudioName = "Lotus", Capacity = 20, IsActive = true });
        var service = new YogaStudioService(repo, Clock);

        var result = await service.CreateAsync(new YogaStudioRequest(" lotus ", 2, 15), "manager", null);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("YOGA_STUDIO_NAME_TAKEN", result.ErrorCode);
    }

    [Fact]
    public async Task StudioWithFutureSessionCannotBeDeactivated()
    {
        var repo = new YogaRepositoryFake();
        var studio = new YogaStudio { Id = 1, GymId = 1, StudioName = "Lotus", Capacity = 20, IsActive = true };
        repo.Studios.Add(studio);
        repo.Sessions.Add(Session(1, studio, Class(), Today.AddDays(3), new(9, 0), new(10, 0)));
        var service = new YogaStudioService(repo, Clock);

        var result = await service.SetActiveAsync(1, false, "manager", null);

        Assert.Equal(409, result.StatusCode);
        Assert.True(studio.IsActive);
    }

    [Fact]
    public async Task StudioWithOnlyPastOrCancelledSessionsCanBeDeactivated()
    {
        var repo = new YogaRepositoryFake();
        var studio = new YogaStudio { Id = 1, GymId = 1, StudioName = "Lotus", Capacity = 20, IsActive = true };
        repo.Studios.Add(studio);
        repo.Sessions.Add(Session(1, studio, Class(), Today.AddDays(-1), new(9, 0), new(10, 0)));
        var cancelled = Session(2, studio, Class(), Today.AddDays(5), new(9, 0), new(10, 0));
        cancelled.ScheduleStatus = YogaScheduleStatus.Cancelled;
        repo.Sessions.Add(cancelled);
        var service = new YogaStudioService(repo, Clock);

        var result = await service.SetActiveAsync(1, false, "manager", null);

        Assert.True(result.IsSuccess);
        Assert.False(studio.IsActive);
        Assert.Single(repo.Audits);
    }

    // ── GBR-73 / GBR-75 validation ──────────────────────────────

    [Theory]
    [InlineData(5, 3, 60, "MaxParticipants")]   // min > max
    [InlineData(0, 10, 60, "MinParticipants")]  // min < 1
    [InlineData(1, 10, 30, "DurationMinutes")]  // too short
    [InlineData(1, 10, 120, "DurationMinutes")] // too long
    public void ClassValidatorRejectsInvalidLimits(int min, int max, int duration, string property)
    {
        var request = ClassRequest() with { MinParticipants = min, MaxParticipants = max, DurationMinutes = duration };
        var result = new YogaClassRequestValidator().Validate(request);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }

    [Fact]
    public void ClassValidatorAcceptsCompleteClass() =>
        Assert.True(new YogaClassRequestValidator().Validate(ClassRequest()).IsValid);

    [Fact]
    public void SessionValidatorRejectsSessionLongerThan90Minutes()
    {
        var request = new YogaSessionRequest(1, 1, Today.AddDays(10), new(9, 0), new(10, 45));
        Assert.False(new YogaSessionRequestValidator().Validate(request).IsValid);
    }

    // ── GBR-74 / GBR-75 overlap ─────────────────────────────────

    [Fact]
    public void SameStudioNeedsFifteenMinuteGap()
    {
        var studio = new YogaStudio { Id = 1 };
        var existing = Session(1, studio, Class("other"), Today, new(9, 0), new(10, 0));

        Assert.NotNull(YogaClassService.FindConflict(1, "pt-1", new(10, 10), new(11, 10), [existing]));
        Assert.Null(YogaClassService.FindConflict(1, "pt-1", new(10, 15), new(11, 15), [existing]));
    }

    [Fact]
    public void SameInstructorCannotOverlapInAnotherStudio()
    {
        var existing = Session(1, new YogaStudio { Id = 2 }, Class("pt-1"), Today, new(9, 0), new(10, 0));

        Assert.NotNull(YogaClassService.FindConflict(1, "pt-1", new(9, 30), new(10, 30), [existing]));
        Assert.Null(YogaClassService.FindConflict(1, "pt-1", new(10, 0), new(11, 0), [existing]));
        Assert.Null(YogaClassService.FindConflict(1, "pt-2", new(9, 30), new(10, 30), [existing]));
    }

    [Fact]
    public void SessionInstructorOverridesClassInstructor()
    {
        var existing = Session(1, new YogaStudio { Id = 2 }, Class("pt-1"), Today, new(9, 0), new(10, 0));
        existing.InstructorUserId = "pt-9";

        Assert.Null(YogaClassService.FindConflict(1, "pt-1", new(9, 30), new(10, 30), [existing]));
        Assert.NotNull(YogaClassService.FindConflict(1, "pt-9", new(9, 30), new(10, 30), [existing]));
    }

    [Fact]
    public async Task CreateSessionRejectsStudioConflict()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        repo.Sessions.Add(Session(50, studio, Class("other"), Today.AddDays(10), new(9, 0), new(10, 0)));
        var service = new YogaClassService(repo, Clock);

        var result = await service.CreateSessionAsync(
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(10), new(9, 30), new(10, 30)), "manager", null);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("YOGA_SESSION_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task CancelledSessionDoesNotBlockStudio()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        var cancelled = Session(50, studio, Class("other"), Today.AddDays(10), new(9, 0), new(10, 0));
        cancelled.ScheduleStatus = YogaScheduleStatus.Cancelled;
        repo.Sessions.Add(cancelled);
        var service = new YogaClassService(repo, Clock);

        var result = await service.CreateSessionAsync(
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(10), new(9, 0), new(10, 0)), "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task SessionMustMatchClassDurationAndDates()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        var service = new YogaClassService(repo, Clock);

        var wrongLength = await service.CreateSessionAsync(
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(10), new(9, 0), new(9, 45)), "manager", null);
        var outside = await service.CreateSessionAsync(
            new YogaSessionRequest(yogaClass.Id, studio.Id, yogaClass.EndDate.AddDays(1), new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_SESSION_DURATION", wrongLength.ErrorCode);
        Assert.Equal("YOGA_SESSION_OUTSIDE_CLASS", outside.ErrorCode);
    }

    // ── GBR-77 publishing ───────────────────────────────────────

    [Fact]
    public async Task PublishRequiresSevenDaysBeforeFirstSession()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        var session = Session(10, studio, yogaClass, Today.AddDays(6), new(9, 0), new(10, 0));
        yogaClass.Schedules.Add(session);
        var service = new YogaClassService(repo, Clock);

        var result = await service.PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.Equal("YOGA_PUBLISH_TOO_LATE", result.ErrorCode);
        Assert.Equal(YogaClassStatus.Draft, yogaClass.ClassStatus);
    }

    [Fact]
    public async Task PublishOpensClassWhenLeadTimeIsMet()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.Schedules.Add(Session(10, studio, yogaClass, Today.AddDays(7), new(9, 0), new(10, 0)));
        var service = new YogaClassService(repo, Clock);

        var result = await service.PublishClassAsync(yogaClass.Id, "manager", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(YogaClassStatus.Open, yogaClass.ClassStatus);
    }

    // ── GBR-76 cancellation ─────────────────────────────────────

    [Fact]
    public async Task CancellingSessionKeepsEnrolmentsAndNotifiesEnrolledMembers()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        var session = Session(10, studio, yogaClass, Today.AddDays(9), new(9, 0), new(10, 0));
        yogaClass.Bookings.Add(new YogaBooking { Id = 1, MemberUserId = "m1", BookingStatus = YogaBookingStatus.Confirmed });
        yogaClass.Bookings.Add(new YogaBooking { Id = 2, MemberUserId = "m2", BookingStatus = YogaBookingStatus.Cancelled });
        repo.Sessions.Add(session);
        var service = new YogaClassService(repo, Clock);

        var result = await service.CancelSessionAsync(10, "manager", null);

        Assert.True(result.IsSuccess);
        Assert.Equal(YogaScheduleStatus.Cancelled, session.ScheduleStatus);
        Assert.Equal(YogaBookingStatus.Confirmed, yogaClass.Bookings.First().BookingStatus);
        var notification = Assert.Single(repo.Notifications);
        Assert.Equal("m1", notification.RecipientUserId);
        Assert.Equal(nameof(NotificationType.YogaSessionCancelled), notification.NotificationType);
    }

    [Fact]
    public async Task CancellingClassCancelsEnrolmentsAndFutureSessions()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Open;
        var session = Session(10, studio, yogaClass, Today.AddDays(9), new(9, 0), new(10, 0));
        yogaClass.Schedules.Add(session);
        yogaClass.Bookings.Add(new YogaBooking { Id = 1, MemberUserId = "m1", BookingStatus = YogaBookingStatus.Confirmed });
        var service = new YogaClassService(repo, Clock);

        var result = await service.CancelClassAsync(yogaClass.Id, "manager", null);

        Assert.True(result.IsSuccess);
        Assert.Equal(YogaScheduleStatus.Cancelled, session.ScheduleStatus);
        Assert.Equal(YogaBookingStatus.Cancelled, yogaClass.Bookings.Single().BookingStatus);
        Assert.Single(repo.Notifications);
    }

    [Fact]
    public async Task PublishedClassCannotGetSessionWithinSevenDays()   // GBR-77
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Open;
        var service = new YogaClassService(repo, Clock);

        var tooSoon = await service.CreateSessionAsync(
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(3), new(9, 0), new(10, 0)), "manager", null);
        var ok = await service.CreateSessionAsync(
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(7), new(9, 0), new(10, 0)), "manager", null);

        Assert.Equal("YOGA_PUBLISH_TOO_LATE", tooSoon.ErrorCode);
        Assert.True(ok.IsSuccess, ok.ErrorMessage);
        Assert.Equal([Today.AddDays(3), Today.AddDays(7)], repo.LockedDates);   // GBR-74/75 check runs under the date lock
    }

    [Fact]
    public async Task PublishedSessionCanGetSubstituteInstructorInsideSevenDays()   // GBR-77 only for new or re-dated sessions
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Open;
        var session = Session(10, studio, yogaClass, Today.AddDays(2), new(9, 0), new(10, 0));
        repo.Sessions.Add(session);
        var service = new YogaClassService(repo, Clock);

        var substitute = await service.UpdateSessionAsync(10,
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(2), new(9, 0), new(10, 0), "pt-2"), "manager", null);
        var moved = await service.UpdateSessionAsync(10,
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(3), new(9, 0), new(10, 0), "pt-2"), "manager", null);
        var retimed = await service.UpdateSessionAsync(10,
            new YogaSessionRequest(yogaClass.Id, studio.Id, Today.AddDays(2), new(18, 0), new(19, 0), "pt-2"), "manager", null);

        Assert.True(substitute.IsSuccess, substitute.ErrorMessage);
        Assert.Equal("pt-2", session.InstructorUserId);
        Assert.Equal("YOGA_PUBLISH_TOO_LATE", moved.ErrorCode);
        Assert.Equal("YOGA_PUBLISH_TOO_LATE", retimed.ErrorCode);
    }

    [Fact]
    public async Task ChangingClassInstructorIsBlockedWhenSessionsInheritIt()   // GBR-74
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.Schedules.Add(Session(10, studio, yogaClass, Today.AddDays(9), new(9, 0), new(10, 0)));
        var service = new YogaClassService(repo, Clock);
        var request = ClassRequest() with { InstructorUserId = "pt-2", StartDate = Today, EndDate = Today.AddDays(30),
            RegistrationDeadline = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) };

        var result = await service.UpdateClassAsync(yogaClass.Id, request, "manager", null);

        Assert.Equal("YOGA_CLASS_INSTRUCTOR_IN_USE", result.ErrorCode);
    }

    [Fact]
    public async Task StudioCapacityCannotDropBelowUpcomingClassSize()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        repo.Sessions.Add(Session(10, studio, yogaClass, Today.AddDays(9), new(9, 0), new(10, 0)));
        var service = new YogaStudioService(repo, Clock);

        var result = await service.UpdateAsync(studio.Id, new YogaStudioRequest("Lotus", 2, yogaClass.MaxParticipants - 1), "manager", null);

        Assert.Equal("YOGA_STUDIO_CAPACITY_IN_USE", result.ErrorCode);
    }

    // ── UC-37 / UC-41 views ─────────────────────────────────────

    [Fact]
    public async Task PublishedScheduleHidesDraftAndCancelledAndCountsFreeSeats()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Open;
        var visible = Session(10, studio, yogaClass, Today.AddDays(1), new(9, 0), new(10, 0));
        yogaClass.Bookings.Add(new YogaBooking { MemberUserId = "m1", BookingStatus = YogaBookingStatus.Confirmed });
        var cancelled = Session(11, studio, yogaClass, Today.AddDays(2), new(9, 0), new(10, 0));
        cancelled.ScheduleStatus = YogaScheduleStatus.Cancelled;
        var draft = Class("pt-1");
        draft.ClassStatus = YogaClassStatus.Draft;
        repo.Sessions.AddRange([visible, cancelled, Session(12, studio, draft, Today.AddDays(3), new(9, 0), new(10, 0))]);
        var service = new YogaClassService(repo, Clock);

        var result = await service.GetPublishedScheduleAsync(new YogaScheduleQuery());

        var session = Assert.Single(result.Data!);
        Assert.Equal(10, session.Id);
        Assert.Equal(yogaClass.MaxParticipants - 1, session.FreeSeats);
    }

    [Fact]
    public async Task AssignedSessionsOnlyShowInstructorsOwnSessionsWithRoster()
    {
        var repo = Seeded(out var yogaClass, out var studio);
        yogaClass.ClassStatus = YogaClassStatus.Open;
        var mine = Session(10, studio, yogaClass, Today.AddDays(1), new(9, 0), new(10, 0));
        yogaClass.Bookings.Add(new YogaBooking { Id = 5, MemberUserId = "m1", MatNumber = 3, BookingStatus = YogaBookingStatus.Confirmed });
        var substitute = Session(11, studio, yogaClass, Today.AddDays(2), new(9, 0), new(10, 0));
        substitute.InstructorUserId = "pt-2";
        repo.Sessions.AddRange([mine, substitute]);
        var service = new YogaClassService(repo, Clock);

        var result = await service.GetAssignedSessionsAsync("pt-1", new YogaScheduleQuery());

        var assigned = Assert.Single(result.Data!);
        Assert.Equal(10, assigned.Session.Id);
        Assert.Equal(3, Assert.Single(assigned.Roster).MatNumber);
    }

    // ── Helpers ─────────────────────────────────────────────────

    [Fact]
    public async Task OnlyYogaInstructorRoleCanTeachAClass()   // GBR-02: PT and Yoga Instructor are separate roles
    {
        var repo = Seeded(out _, out _);
        repo.Roles["gym-pt"] = "PT";
        var service = new YogaClassService(repo, Clock);

        var personalTrainer = await service.CreateClassAsync(ClassRequest() with { InstructorUserId = "gym-pt" }, "manager", null);
        var yogaInstructor = await service.CreateClassAsync(ClassRequest(), "manager", null);

        Assert.Equal("YOGA_INVALID_INSTRUCTOR", personalTrainer.ErrorCode);
        Assert.True(yogaInstructor.IsSuccess, yogaInstructor.ErrorMessage);
        Assert.Equal("YogaInstructor", YogaClassService.InstructorRole);
    }

    private static YogaRepositoryFake Seeded(out YogaClass yogaClass, out YogaStudio studio)
    {
        var repo = new YogaRepositoryFake();
        studio = new YogaStudio { Id = 1, GymId = 1, StudioName = "Lotus", FloorNumber = 2, Capacity = 20, IsActive = true };
        yogaClass = Class("pt-1");
        yogaClass.Id = 7;
        repo.Studios.Add(studio);
        repo.Classes.Add(yogaClass);
        repo.Roles["pt-1"] = YogaClassService.InstructorRole;
        repo.Roles["pt-2"] = YogaClassService.InstructorRole;
        return repo;
    }

    private static YogaClass Class(string instructor = "pt-1") => new()
    {
        Id = instructor.GetHashCode() & 0xffff,
        ClassName = "Morning Flow",
        InstructorUserId = instructor,
        YogaClassType = new YogaClassType { TypeName = "Vinyasa" },
        DifficultyLevel = "Beginner",
        MinParticipants = 3,
        MaxParticipants = 15,
        DurationMinutes = 60,
        StartDate = Today,
        EndDate = Today.AddDays(30),
        RegistrationDeadline = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
        ClassStatus = YogaClassStatus.Draft
    };

    private static YogaClassSchedule Session(int id, YogaStudio studio, YogaClass yogaClass, DateOnly date, TimeOnly start, TimeOnly end) => new()
    {
        Id = id,
        YogaStudioId = studio.Id,
        YogaStudio = studio,
        YogaClassId = yogaClass.Id,
        YogaClass = yogaClass,
        ScheduledDate = date,
        StartTime = start,
        EndTime = end,
        ScheduleStatus = YogaScheduleStatus.Scheduled
    };

    private static YogaClassRequest ClassRequest() => new(
        1, "Morning Flow", "pt-1", "Beginner", 2, 3, 15, 60,
        new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), Today.AddDays(10), Today.AddDays(40));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class YogaRepositoryFake : IYogaRepository
    {
        public List<YogaStudio> Studios { get; } = [];
        public List<YogaClass> Classes { get; } = [];
        public List<YogaClassSchedule> Sessions { get; } = [];
        public Dictionary<string, string> Roles { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public List<Notification> Notifications { get; } = [];
        private int _nextId = 1000;

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            switch (entity)
            {
                case AuditLog audit: Audits.Add(audit); break;
                case Notification notification: Notifications.Add(notification); break;
                case YogaStudio studio: studio.Id = _nextId++; Studios.Add(studio); break;
                case YogaClass yogaClass: yogaClass.Id = _nextId++; Classes.Add(yogaClass); break;
                case YogaClassSchedule session:
                    session.Id = _nextId++;
                    session.YogaClass = Classes.First(c => c.Id == session.YogaClassId);
                    session.YogaStudio = Studios.First(s => s.Id == session.YogaStudioId);
                    Sessions.Add(session);
                    break;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Roles.Where(r => r.Value == roleName).Select(r => r.Key).ToArray());
        public Task<int?> GetDefaultGymIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(1);

        public Task<IReadOnlyList<YogaStudio>> GetStudiosAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaStudio>>(Studios.Where(s => includeInactive || s.IsActive).ToArray());
        public Task<YogaStudio?> GetStudioAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Studios.FirstOrDefault(s => s.Id == id));
        public Task<bool> StudioNameExistsAsync(int gymId, string name, int? excludingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Studios.Any(s => s.GymId == gymId && s.StudioName.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Id != excludingId));
        public Task<int> GetLargestClassUsingStudioAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.Where(s => s.YogaStudioId == studioId && s.ScheduledDate >= fromDate && s.ScheduleStatus == YogaScheduleStatus.Scheduled)
                .Select(s => s.YogaClass.MaxParticipants).DefaultIfEmpty(0).Max());
        public Task<bool> StudioHasFutureSessionsAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.Any(s => s.YogaStudioId == studioId && s.ScheduledDate >= fromDate && s.ScheduleStatus == YogaScheduleStatus.Scheduled));

        public Task<IReadOnlyList<YogaClassType>> GetClassTypesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClassType>>([]);
        public Task<YogaClassType?> GetClassTypeAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<YogaClassType?>(new YogaClassType { Id = id, TypeName = "Vinyasa" });
        public Task<bool> ClassTypeNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<YogaClass>> GetClassesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClass>>(Classes);
        public Task<YogaClass?> GetClassAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Classes.FirstOrDefault(c => c.Id == id));

        public Task<YogaClassSchedule?> GetSessionAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id));
        public Task<IReadOnlyList<YogaClassSchedule>> GetActiveSessionsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClassSchedule>>(
                Sessions.Where(s => s.ScheduledDate == date && s.ScheduleStatus != YogaScheduleStatus.Cancelled).ToArray());
        public List<DateOnly> LockedDates { get; } = [];
        public Task LockScheduleDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            LockedDates.Add(date);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<YogaClassSchedule>> GetSessionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClassSchedule>>(Sessions.Where(s => s.ScheduledDate >= from && s.ScheduledDate <= to).ToArray());

        public Task<IReadOnlyList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationUser>>(
                Roles.Where(r => r.Value == roleName).Select(r => new ApplicationUser { UserId = r.Key, FullName = r.Key }).ToArray());
        public Task<IReadOnlyDictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.Distinct().ToDictionary(id => id, id => "Name " + id));
        public Task<bool> UserHasRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Roles.TryGetValue(userId, out var role) && role == roleName);
    }
}
