using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;
using VFMS.Contracts.Yoga.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

/// <summary>GBR-128..GBR-130: a Yoga Instructor asks to open a class; the Manager approves (creates the Draft class) or rejects.</summary>
public sealed class YogaClassOpeningTests
{
    // 2026-11-02 03:00 UTC = 10:00 Monday at the gym (UTC+7).
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 11, 2);

    private readonly OpeningFake _requests = new();
    private readonly YogaFake _yoga = new();
    private readonly YogaClassOpeningService _service;

    public YogaClassOpeningTests()
    {
        _yoga.Studios.Add(new YogaStudio { Id = 1, StudioName = "Lotus", Capacity = 12, IsActive = true });
        _yoga.Studios.Add(new YogaStudio { Id = 2, StudioName = "Closed", Capacity = 30, IsActive = false });
        _yoga.Roles["yi-1"] = YogaClassService.InstructorRole;
        _yoga.Roles["yi-2"] = YogaClassService.InstructorRole;
        _requests.Managers.AddRange(["m-1", "m-2"]);
        _service = new YogaClassOpeningService(_requests, _yoga, new YogaClassService(_yoga, Clock), Clock);
    }

    private static YogaClassOpeningRequestRequest Submit(DateOnly? start = null, int? studio = 1, int max = 12) => new(
        5, " Sunrise Flow ", "intermediate", 2, 4, max, 60, start ?? Today.AddDays(7), Today.AddDays(60),
        [DayOfWeek.Wednesday, DayOfWeek.Monday], new TimeOnly(6, 30), studio, null, "  members asked  ");

    private static YogaClassRequest Form(DateOnly? start = null, string instructor = "yi-1") => new(
        5, "Sunrise Flow", instructor, "Intermediate", 2, 4, 12, 60,
        new DateTime(2026, 11, 8, 16, 59, 0, DateTimeKind.Utc), start ?? Today.AddDays(7), Today.AddDays(60));

    // ── GBR-128 submit ───────────────────────────────────────────

    [Fact]
    public async Task SubmitSavesAPendingRequestAndNotifiesManagers()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(201, result.StatusCode);
        var saved = Assert.Single(_requests.Items);
        Assert.Equal(YogaClassRequestStatus.Pending, saved.RequestStatus);
        Assert.Equal("yi-1", saved.InstructorUserId);
        Assert.Equal("Sunrise Flow", saved.ClassName);
        Assert.Equal("Intermediate", saved.DifficultyLevel);
        Assert.Equal("Monday,Wednesday", saved.PreferredDays);   // week order
        Assert.Equal("members asked", saved.Note);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], result.Data!.PreferredDays);
        Assert.Equal(["m-1", "m-2"], _requests.Notifications.Select(n => n.RecipientUserId));
        Assert.All(_requests.Notifications, n => Assert.Equal(nameof(NotificationType.YogaClassRequest), n.NotificationType));
        Assert.Single(_requests.Audits);
    }

    [Fact]
    public async Task StartMustBeAtLeastSevenDaysAway()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(start: Today.AddDays(6)), null);

        Assert.Equal("YOGA_REQUEST_START_TOO_SOON", result.ErrorCode);
        Assert.Empty(_requests.Items);
    }

    [Theory]
    [InlineData(1, 13, "YOGA_STUDIO_TOO_SMALL")]
    [InlineData(2, 10, "YOGA_STUDIO_UNAVAILABLE")]
    [InlineData(99, 10, "YOGA_STUDIO_UNAVAILABLE")]
    public async Task PreferredStudioMustBeActiveAndBigEnough(int studio, int max, string code)
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: studio, max: max), null);

        Assert.Equal(code, result.ErrorCode);
        Assert.Empty(_requests.Items);
    }

    [Fact]
    public async Task NoStudioLeavesTheChoiceToTheManager()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: null, max: 40), null);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task AtMostThreePendingRequestsPerInstructor()
    {
        for (var i = 0; i < YogaClassOpeningService.MaxPendingPerInstructor; i++)
            Assert.True((await _service.SubmitAsync("yi-1", Submit(), null)).IsSuccess);

        var fourth = await _service.SubmitAsync("yi-1", Submit(), null);
        var other = await _service.SubmitAsync("yi-2", Submit(), null);

        Assert.Equal(409, fourth.StatusCode);
        Assert.Equal("YOGA_REQUEST_PENDING_LIMIT", fourth.ErrorCode);
        Assert.True(other.IsSuccess);
    }

    [Theory]
    [InlineData(2, new[] { DayOfWeek.Monday })]
    [InlineData(2, new[] { DayOfWeek.Monday, DayOfWeek.Monday })]
    [InlineData(1, new[] { DayOfWeek.Monday, DayOfWeek.Friday })]
    public void ValidatorNeedsOneDistinctDayPerWeeklySession(int perWeek, DayOfWeek[] days)
    {
        var result = new YogaClassOpeningRequestRequestValidator().Validate(Submit() with { SessionsPerWeek = perWeek, PreferredDays = days });
        Assert.Contains(result.Errors, e => e.PropertyName == "PreferredDays");
    }

    [Fact]
    public void ValidatorAcceptsAValidRequest() =>
        Assert.True(new YogaClassOpeningRequestRequestValidator().Validate(Submit()).IsValid);

    // ── GBR-129 / GBR-130 decide ─────────────────────────────────

    [Fact]
    public async Task ApproveCreatesTheDraftClassAndNotifiesTheInstructor()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;
        _requests.Notifications.Clear();

        var result = await _service.ApproveAsync(id, Form(), "m-1", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(201, result.StatusCode);
        var yogaClass = Assert.Single(_yoga.Classes);
        Assert.Equal(YogaClassStatus.Draft, yogaClass.ClassStatus);
        Assert.Equal(("Sunrise Flow", "yi-1"), (yogaClass.ClassName, yogaClass.InstructorUserId));
        var request = _requests.Items[0];
        Assert.Equal(YogaClassRequestStatus.Approved, request.RequestStatus);
        Assert.Equal(yogaClass.Id, request.YogaClassId);
        Assert.Equal("m-1", request.ReviewedByUserId);
        Assert.Equal("yi-1", Assert.Single(_requests.Notifications).RecipientUserId);
        Assert.Contains(id, _requests.Locked);
    }

    [Fact]
    public async Task ManagerCanChangeTheFormBeforeApproving()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;

        var result = await _service.ApproveAsync(id, Form(instructor: "yi-2") with { ClassName = "Sunrise Flow B" }, "m-1", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(("Sunrise Flow B", "yi-2"), (_yoga.Classes[0].ClassName, _yoga.Classes[0].InstructorUserId));
        Assert.Equal("yi-1", _requests.Notifications.Last().RecipientUserId);   // the requester hears the decision
    }

    [Fact]
    public async Task InvalidFormLeavesTheRequestPending()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;

        var result = await _service.ApproveAsync(id, Form(start: Today.AddDays(-1)), "m-1", null);

        Assert.Equal("YOGA_CLASS_START_PAST", result.ErrorCode);
        Assert.Empty(_yoga.Classes);
        Assert.Equal(YogaClassRequestStatus.Pending, _requests.Items[0].RequestStatus);
    }

    [Fact]
    public async Task ADecidedRequestCannotBeDecidedAgain()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;
        Assert.True((await _service.ApproveAsync(id, Form(), "m-1", null)).IsSuccess);

        var again = await _service.ApproveAsync(id, Form(), "m-2", null);
        var reject = await _service.RejectAsync(id, "late", "m-2", null);
        var withdraw = await _service.WithdrawAsync("yi-1", id, null);

        Assert.Equal((409, 409, 409), (again.StatusCode, reject.StatusCode, withdraw.StatusCode));
        Assert.Equal("YOGA_REQUEST_ALREADY_DECIDED", again.ErrorCode);
        Assert.Single(_yoga.Classes);
    }

    [Fact]
    public async Task RejectNeedsAReasonAndSendsItToTheInstructor()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;
        _requests.Notifications.Clear();

        var blank = await _service.RejectAsync(id, "  ", "m-1", null);
        var result = await _service.RejectAsync(id, " Studio is booked that month ", "m-1", null);

        Assert.Equal("YOGA_REQUEST_REASON_REQUIRED", blank.ErrorCode);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Rejected", result.Data!.Status);
        Assert.Equal("Studio is booked that month", result.Data.RejectReason);
        var notice = Assert.Single(_requests.Notifications);
        Assert.Equal("yi-1", notice.RecipientUserId);
        Assert.Contains("Studio is booked that month", notice.Message);
        Assert.Empty(_yoga.Classes);
    }

    [Fact]
    public void RejectValidatorNeedsAReason() =>
        Assert.False(new RejectYogaClassOpeningRequestValidator().Validate(new RejectYogaClassOpeningRequest("")).IsValid);

    [Fact]
    public async Task OnlyTheRequesterCanWithdrawAndItFreesAPendingSlot()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;

        var stranger = await _service.WithdrawAsync("yi-2", id, null);
        var own = await _service.WithdrawAsync("yi-1", id, null);

        Assert.Equal(404, stranger.StatusCode);
        Assert.Equal("Withdrawn", own.Data!.Status);
        Assert.Equal(0, await _requests.CountPendingAsync("yi-1"));
    }

    [Fact]
    public async Task ManagerListFiltersByStatus()
    {
        await _service.SubmitAsync("yi-1", Submit(), null);
        var second = (await _service.SubmitAsync("yi-2", Submit(), null)).Data!.Id;
        await _service.RejectAsync(second, "no", "m-1", null);

        Assert.Single((await _service.GetAllAsync("pending")).Data!);
        Assert.Single((await _service.GetAllAsync("Rejected")).Data!);
        Assert.Equal(2, (await _service.GetAllAsync(null)).Data!.Count);
        Assert.Equal("YOGA_REQUEST_INVALID_STATUS", (await _service.GetAllAsync("Lost")).ErrorCode);
        Assert.Single((await _service.GetMineAsync("yi-2")).Data!);
    }

    // ── Fakes ────────────────────────────────────────────────────

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OpeningFake : IYogaClassOpeningRepository
    {
        public List<YogaClassOpeningRequest> Items { get; } = [];
        public List<Notification> Notifications { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public List<string> Managers { get; } = [];
        public List<int> Locked { get; } = [];
        private int _nextId = 1;

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            switch (entity)
            {
                case AuditLog audit: Audits.Add(audit); break;
                case Notification notification: Notifications.Add(notification); break;
                case YogaClassOpeningRequest request: request.Id = _nextId++; Items.Add(request); break;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(roleName == "Manager" ? Managers : []);
        public Task<YogaClassOpeningRequest?> GetAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<YogaClassOpeningRequest>> ListAsync(
            string? instructorUserId, YogaClassRequestStatus? status, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClassOpeningRequest>>(Items
                .Where(r => (instructorUserId == null || r.InstructorUserId == instructorUserId) && (status == null || r.RequestStatus == status))
                .OrderByDescending(r => r.Id).ToArray());
        public Task<int> CountPendingAsync(string instructorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Count(r => r.InstructorUserId == instructorUserId && r.RequestStatus == YogaClassRequestStatus.Pending));
        public Task LockAsync(int id, CancellationToken cancellationToken = default)
        {
            Locked.Add(id);
            return Task.CompletedTask;
        }
    }

    /// <summary>Just what class creation and the request form need.</summary>
    private sealed class YogaFake : IYogaRepository
    {
        public List<YogaStudio> Studios { get; } = [];
        public List<YogaClass> Classes { get; } = [];
        public Dictionary<string, string> Roles { get; } = [];
        private int _nextId = 100;

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity is YogaClass yogaClass)
            {
                yogaClass.Id = _nextId++;
                Classes.Add(yogaClass);
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
            throw new NotSupportedException();
        public Task<int> GetLargestClassUsingStudioAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> StudioHasFutureSessionsAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<YogaClassType>> GetClassTypesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClassType>>([new YogaClassType { Id = 5, TypeName = "Hatha" }]);
        public Task<YogaClassType?> GetClassTypeAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == 5 ? new YogaClassType { Id = 5, TypeName = "Hatha" } : null);
        public Task<bool> ClassTypeNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<YogaClass>> GetClassesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaClass>>(Classes);
        public Task<YogaClass?> GetClassAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Classes.FirstOrDefault(c => c.Id == id));
        public Task<YogaClassSchedule?> GetSessionAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<YogaClassSchedule>> GetActiveSessionsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task LockScheduleDateAsync(DateOnly date, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<YogaClassSchedule>> GetSessionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationUser>>(
                Roles.Where(r => r.Value == roleName).Select(r => new ApplicationUser { UserId = r.Key, FullName = r.Key }).ToArray());
        public Task<IReadOnlyDictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.Distinct().ToDictionary(id => id, id => "Name " + id));
        public Task<bool> UserHasRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Roles.TryGetValue(userId, out var role) && role == roleName);
    }
}
