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
        public Task<IReadOnlyList<YogaWaitlist>> GetWaitlistAsync(int classId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<YogaWaitlist>>(Waitlist.Where(w => w.WaitlistStatus == YogaWaitlistStatus.Waiting).OrderBy(w => w.Position).ToList());
    }
}
