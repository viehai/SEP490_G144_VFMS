using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.CheckIn.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class CheckInTests
{
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero));
    private static float[] Descriptor(float v = 0.1f) => Enumerable.Repeat(v, 128).ToArray();

    [Fact]
    public void ValidatorRequiresConsentAnd128FiniteValues()
    {
        var validator = new RegisterFaceRequestValidator();
        Assert.True(validator.Validate(new RegisterFaceRequest(Descriptor(), true)).IsValid);
        Assert.False(validator.Validate(new RegisterFaceRequest(Descriptor(), false)).IsValid);
        Assert.False(validator.Validate(new RegisterFaceRequest(new float[64], true)).IsValid);
        var bad = Descriptor();
        bad[3] = float.NaN;
        Assert.False(validator.Validate(new RegisterFaceRequest(bad, true)).IsValid);
    }

    [Fact]
    public async Task RegisterStoresOnlyEncryptedTemplateWithConsentTime()
    {
        var repo = new RepoFake();
        var service = new FaceProfileService(repo, new ProtectorFake(), Clock);

        var result = await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);

        Assert.Equal(201, result.StatusCode);
        Assert.True(result.Data!.IsRegistered);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, repo.Face!.ConsentGrantedAt);
        Assert.Equal(ProtectorFake.Marker, repo.Face.FaceNetEmbedding[0]);
        Assert.DoesNotContain("0.1", Assert.Single(repo.Audits).NewValue);
    }

    [Fact]
    public async Task RegisterWithoutConsentIsRejected()
    {
        var repo = new RepoFake();
        var result = await new FaceProfileService(repo, new ProtectorFake(), Clock)
            .RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), false), null);
        Assert.Equal("FACE_CONSENT_REQUIRED", result.ErrorCode);
        Assert.Null(repo.Face);
    }

    [Fact]
    public async Task DeleteErasesTemplateAndReRegisterReusesRecord()
    {
        var repo = new RepoFake();
        var service = new FaceProfileService(repo, new ProtectorFake(), Clock);
        await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);

        var deleted = await service.DeleteAsync("u1", null);

        Assert.False(deleted.Data!.IsRegistered);
        Assert.Empty(repo.Face!.FaceNetEmbedding);
        Assert.False(repo.Face.IsActive);
        Assert.Equal(404, (await service.DeleteAsync("u1", null)).StatusCode);

        var again = await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(0.2f), true), null);
        Assert.Equal(200, again.StatusCode);
        Assert.True(repo.Face.IsActive);
        Assert.False(repo.Face.IsDeleted);
    }

    [Fact]
    public async Task ActiveCheckInsShowOccupancyDurationAndSearch()
    {
        var repo = new RepoFake();
        repo.Active.Add(new CheckIn
        {
            Id = 1, MemberUserId = "m1", Member = new ApplicationUser { FullName = "An Nguyen", PhoneNumber = "0901" },
            CheckinTime = Clock.GetUtcNow().UtcDateTime.AddMinutes(-45), CheckinMethod = CheckInMethod.FaceRecognition, Status = CheckInStatus.Active
        });
        repo.Active.Add(new CheckIn
        {
            Id = 2, MemberUserId = "m2", Member = new ApplicationUser { FullName = "Binh Tran", PhoneNumber = "0902" },
            CheckinTime = Clock.GetUtcNow().UtcDateTime.AddMinutes(-10), CheckinMethod = CheckInMethod.ManualStaff,
            HandledByUser = new ApplicationUser { FullName = "Staff A" }, Status = CheckInStatus.Active,
            MemberMembership = new MemberMembership { OrderItem = new OrderItem { Order = new Order { OrderNumber = "ORD-20261107-AB12" } } }
        });
        repo.TodayCount = 7;
        var service = new ActiveCheckInService(repo, Clock, peakThreshold: 2);

        var all = (await service.GetActiveAsync(null)).Data!;
        var filtered = (await service.GetActiveAsync("binh")).Data!;

        Assert.Equal(2, all.CurrentOccupancy);
        Assert.Equal(7, all.CheckInsToday);
        Assert.Equal(45, all.Items[0].MinutesInside);
        Assert.Equal("Staff A", Assert.Single(filtered.Items).HandledByName);
        var byNumber = (await service.GetActiveAsync("ord-20261107-ab12")).Data!;   // membership (order) number, UC "View Active Check-ins"
        Assert.Equal(2, Assert.Single(byNumber.Items).CheckInId);
        Assert.Equal(2, filtered.CurrentOccupancy);
        Assert.True(all.IsPeak);   // 2 inside, threshold 2
        Assert.False((await new ActiveCheckInService(repo, Clock, peakThreshold: 3).GetActiveAsync(null)).Data!.IsPeak);
    }

    // ── UC-44 kiosk check-in ─────────────────────────────────────

    private static DateTime Now => Clock.GetUtcNow().UtcDateTime;
    private static readonly DateOnly Today = new(2026, 11, 7);   // 03:00 UTC = 10:00 gym time

    private static MemberMembership Membership(string user, MembershipStatus status = MembershipStatus.Active,
        int startOffset = -10, int endOffset = 20, ComponentType type = ComponentType.GYM) => new()
    {
        Id = 7, MemberUserId = user, MembershipStatus = status,
        StartDate = Today.AddDays(startOffset), EndDate = Today.AddDays(endOffset),
        PackageComponent = new PackageComponent { ComponentType = type }
    };

    private static (RepoFake Repo, CheckInService Service, MemberQrService Qr) Kiosk()
    {
        var repo = new RepoFake();
        var qr = new MemberQrService(repo, Clock);
        return (repo, new CheckInService(repo, new ProtectorFake(), qr, Clock), qr);
    }

    private static async Task<KioskScanRequest> QrScan(MemberQrService qr, string user) =>
        new(null, (await qr.IssueAsync(user)).Data!.Token);

    [Fact]
    public async Task KioskMatchesFaceAndChecksInActiveMember()
    {
        var (repo, service, _) = Kiosk();
        repo.Memberships.Add(Membership("m1"));
        repo.Faces.Add(new FaceData { UserId = "m1", IsActive = true, FaceNetEmbedding = new ProtectorFake().Protect(Descriptor(0.1f)) });
        repo.Faces.Add(new FaceData { UserId = "m2", IsActive = true, FaceNetEmbedding = new ProtectorFake().Protect(Descriptor(0.3f)) });

        var ok = await service.KioskCheckInAsync(new KioskScanRequest(Descriptor(0.11f), null));
        var stranger = await service.KioskCheckInAsync(new KioskScanRequest(Descriptor(-0.5f), null));

        Assert.Equal(201, ok.StatusCode);
        Assert.Equal("m1", ok.Data!.MemberUserId);
        Assert.StartsWith("Welcome back, Member m1!", ok.Data.Message);
        Assert.Equal(Today.AddDays(20), ok.Data.ValidThrough);
        var visit = repo.Active.Single(c => c.MemberUserId == "m1");
        Assert.Equal(CheckInMethod.FaceRecognition, visit.CheckinMethod);
        Assert.Equal(7, visit.MemberMembershipId);
        Assert.Null(visit.HandledByUserId);
        Assert.Equal(Now, repo.Faces[0].LastUsedAt);
        Assert.Equal(403, stranger.StatusCode);
    }

    [Theory]
    [InlineData(MembershipStatus.Frozen, "Frozen")]
    [InlineData(MembershipStatus.Suspended, "Suspended")]
    [InlineData(MembershipStatus.Active, "Expired")]
    public async Task RefusedCheckInNamesTheMembershipStatus(MembershipStatus status, string shown)   // MSG-36 "with the status"
    {
        var (repo, service, qr) = Kiosk();
        repo.Memberships.Add(Membership("m1", status, -40, status == MembershipStatus.Active ? -1 : 20));

        var result = await service.KioskCheckInAsync(await QrScan(qr, "m1"));

        Assert.Equal(403, result.StatusCode);
        Assert.EndsWith($"Please contact the front desk. Membership status: {shown}.", result.ErrorMessage);
    }

    [Fact]
    public async Task ACustomerCanOnlyScanThemselves()   // kiosk open to Customer/Member: no checking other people in
    {
        var (repo, service, qr) = Kiosk();
        repo.Memberships.Add(Membership("m1", MembershipStatus.Active, -10, 20));

        var other = await service.KioskCheckInAsync(await QrScan(qr, "m1"), selfUserId: "m2");
        var self = await service.KioskCheckInAsync(await QrScan(qr, "m1"), selfUserId: "m1");

        Assert.Equal(403, other.StatusCode);
        Assert.Equal(201, self.StatusCode);
        Assert.Equal(403, (await service.KioskCheckOutAsync(await QrScan(qr, "m1"), selfUserId: "m2")).StatusCode);
    }

    [Fact]
    public async Task OneScanChecksInThenOut()   // kiosk decides the direction from whether the member is inside
    {
        var (repo, service, qr) = Kiosk();
        repo.Memberships.Add(Membership("m1", MembershipStatus.Active, -10, 20));

        var first = await service.KioskScanAsync(await QrScan(qr, "m1"));
        var second = await service.KioskScanAsync(await QrScan(qr, "m1"));
        var stranger = await service.KioskScanAsync(await QrScan(qr, "m1"), selfUserId: "m2");

        Assert.Equal(("CheckIn", 201), (first.Data!.Action, first.StatusCode));
        Assert.NotNull(first.Data.CheckIn);
        Assert.Equal("CheckOut", second.Data!.Action);
        Assert.NotNull(second.Data.CheckOut);
        Assert.Equal(403, stranger.StatusCode);
    }

    [Theory]
    [InlineData(MembershipStatus.Frozen, -10, 20, 403)]
    [InlineData(MembershipStatus.Active, -40, -1, 403)]      // Active but past its end date = expired
    [InlineData(MembershipStatus.Suspended, -10, 20, 403)]
    [InlineData(MembershipStatus.Scheduled, 0, 30, 201)]     // starts today, worker has not flipped it yet
    [InlineData(MembershipStatus.Scheduled, 1, 30, 403)]
    public async Task OnlyAUsableMembershipOpensTheGate(MembershipStatus status, int start, int end, int expected)   // GBR-89
    {
        var (repo, service, qr) = Kiosk();
        repo.Memberships.Add(Membership("m1", status, start, end));

        var result = await service.KioskCheckInAsync(await QrScan(qr, "m1"));

        Assert.Equal(expected, result.StatusCode);
    }

    [Fact]
    public async Task AntiPassbackBlocksReEntryAndStaleVisitIsClosed()   // GBR-90, JOB-04
    {
        var (repo, service, qr) = Kiosk();
        repo.Memberships.Add(Membership("m1"));
        repo.Active.Add(new CheckIn { MemberUserId = "m1", CheckinTime = Now.AddMinutes(-5), Status = CheckInStatus.CheckedOut });

        var blocked = await service.KioskCheckInAsync(await QrScan(qr, "m1"));
        Assert.Equal("ANTI_PASSBACK", blocked.ErrorCode);
        Assert.Contains("10 minutes", blocked.ErrorMessage);

        repo.Active.Clear();
        var open = new CheckIn { MemberUserId = "m1", CheckinTime = Now.AddHours(-2), Status = CheckInStatus.Active };
        repo.Active.Add(open);
        Assert.Equal("ALREADY_CHECKED_IN", (await service.KioskCheckInAsync(await QrScan(qr, "m1"))).ErrorCode);

        open.CheckinTime = Now.AddHours(-7);
        var ok = await service.KioskCheckInAsync(await QrScan(qr, "m1"));
        Assert.Equal(201, ok.StatusCode);
        Assert.Equal(CheckInStatus.AutoClosed, open.Status);
        Assert.Equal(open.CheckinTime.AddHours(6), open.CheckoutTime);
    }

    [Fact]
    public async Task QrTokenIsSingleUseShortLivedAndReplacedOnReissue()
    {
        var repo = new RepoFake();
        var qr = new MemberQrService(repo, Clock);
        var first = (await qr.IssueAsync("m1")).Data!;
        var second = (await qr.IssueAsync("m1")).Data!;

        Assert.Equal(64, second.Token.Length);
        Assert.Equal(Now + MemberQrService.Lifetime, second.ExpiresAt);
        Assert.Null(await qr.RedeemAsync(first.Token));   // replaced by the reissue
        Assert.Equal("m1", await qr.RedeemAsync(second.Token));
        Assert.Null(await qr.RedeemAsync(second.Token));  // used up

        var third = (await qr.IssueAsync("m1")).Data!;
        var later = new MemberQrService(repo, new FixedClock(Clock.GetUtcNow() + MemberQrService.Lifetime));
        Assert.Null(await later.RedeemAsync(third.Token));
    }

    [Theory]
    [InlineData(25, true)]    // starts in 25 minutes
    [InlineData(-15, true)]   // started 15 minutes ago
    [InlineData(40, false)]
    [InlineData(-20, false)]
    public async Task SelfCheckInCompletesPtSessionInsideTheWindow(int startsInMinutes, bool completes)   // GBR-92
    {
        var (repo, service, qr) = Kiosk();
        repo.Memberships.Add(Membership("m1"));
        var start = TimeOnly.FromDateTime(Now.AddHours(7).AddMinutes(startsInMinutes));
        var pt = new PTSession { Id = 9, MemberUserId = "m1", SessionDate = Today, StartTime = start, PTPurchase = new PTPurchase { RemainingSessions = 3 } };
        repo.PtSessions.Add(pt);

        var result = await service.KioskCheckInAsync(await QrScan(qr, "m1"));

        Assert.Equal(completes ? 9 : null, result.Data!.CompletedPtSessionId);
        Assert.Equal(completes ? PTSessionStatus.Completed : PTSessionStatus.Scheduled, pt.SessionStatus);
        Assert.Equal(completes ? 2 : 3, pt.PTPurchase.RemainingSessions);
    }

    [Fact]
    public async Task FrontDeskSearchShowsEntryStatusAndManualCheckInRecordsStaff()   // UC-46
    {
        var (repo, service, _) = Kiosk();
        repo.Memberships.Add(Membership("m-active"));
        repo.Memberships.Add(Membership("m-expired", MembershipStatus.Active, -40, -1));
        repo.Memberships.Add(Membership("m-frozen", MembershipStatus.Frozen));
        repo.PtSessions.Add(new PTSession { MemberUserId = "m-active", SessionDate = Today, StartTime = new TimeOnly(10, 10), PTPurchase = new PTPurchase() });

        var found = (await service.SearchMembersAsync("m-")).Data!.ToDictionary(r => r.MemberUserId);
        Assert.Equal(("Active", "Expired", "Frozen"), (found["m-active"].MembershipStatus, found["m-expired"].MembershipStatus, found["m-frozen"].MembershipStatus));
        Assert.Equal(404, (await service.SearchMembersAsync("nobody")).StatusCode);   // MSG-01

        var ok = await service.ManualCheckInAsync("m-active", "staff-1");
        var refused = await service.ManualCheckInAsync("m-frozen", "staff-1");

        Assert.Equal(201, ok.StatusCode);
        Assert.Null(ok.Data!.CompletedPtSessionId);   // GBR-92 is for self check-in only
        var visit = repo.Active.Single(c => c.MemberUserId == "m-active");
        Assert.Equal((CheckInMethod.ManualStaff, "staff-1"), (visit.CheckinMethod, visit.HandledByUserId));
        Assert.Equal(403, refused.StatusCode);
        Assert.True((await service.SearchMembersAsync("m-active")).Data!.Single().IsCheckedIn);
    }

    [Fact]
    public async Task KioskCheckOutClosesTheOpenVisitOrRefuses()   // UC-45, GBR-91
    {
        var (repo, service, qr) = Kiosk();
        var open = new CheckIn { Id = 5, MemberUserId = "m1", CheckinTime = Now.AddMinutes(-90), Status = CheckInStatus.Active };
        repo.Active.Add(open);

        var result = await service.KioskCheckOutAsync(await QrScan(qr, "m1"));
        var none = await service.KioskCheckOutAsync(await QrScan(qr, "m1"));

        Assert.Equal(90, result.Data!.MinutesInside);
        Assert.Equal((CheckInStatus.CheckedOut, Now, CheckInMethod.QRCode), (open.Status, open.CheckoutTime!.Value, open.CheckoutMethod!.Value));
        Assert.Equal((403, "ACCESS_DENIED"), (none.StatusCode, none.ErrorCode));   // MSG-36: no open visit
    }

    [Fact]
    public async Task ManualCheckOutRecordsStaffAndRefusesClosedVisit()   // UC-47, GBR-91
    {
        var (repo, service, _) = Kiosk();
        var open = new CheckIn { Id = 5, MemberUserId = "m1", CheckinTime = Now.AddMinutes(-30), Status = CheckInStatus.Active, HandledByUserId = "staff-in" };
        repo.Active.Add(open);

        var result = await service.ManualCheckOutAsync(5, "staff-out");
        var again = await service.ManualCheckOutAsync(5, "staff-out");

        Assert.True(result.IsSuccess);
        Assert.Equal((CheckInStatus.CheckedOut, CheckInMethod.ManualStaff), (open.Status, open.CheckoutMethod!.Value));
        Assert.Equal(("staff-in", "staff-out"), (open.HandledByUserId, open.CheckedOutByUserId));
        Assert.Equal(404, again.StatusCode);
        Assert.Equal(404, (await service.ManualCheckOutAsync(99, "staff-out")).StatusCode);
    }

    [Fact]
    public void KioskScanNeedsExactlyOneOfFaceOrQr()
    {
        var validator = new KioskScanRequestValidator();
        Assert.True(validator.Validate(new KioskScanRequest(Descriptor(), null)).IsValid);
        Assert.True(validator.Validate(new KioskScanRequest(null, "ABC")).IsValid);
        Assert.False(validator.Validate(new KioskScanRequest(null, null)).IsValid);
        Assert.False(validator.Validate(new KioskScanRequest(Descriptor(), "ABC")).IsValid);
        Assert.False(validator.Validate(new KioskScanRequest(new float[3], null)).IsValid);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ProtectorFake : IFaceTemplateProtector
    {
        public const byte Marker = 0xEE;
        public byte[] Protect(IReadOnlyList<float> descriptor) => [Marker, .. descriptor.SelectMany(BitConverter.GetBytes)];
        public float[] Unprotect(byte[] protectedTemplate) =>
            Enumerable.Range(0, (protectedTemplate.Length - 1) / 4).Select(i => BitConverter.ToSingle(protectedTemplate, 1 + i * 4)).ToArray();
    }

    private sealed class RepoFake : ICheckInRepository
    {
        public FaceData? Face { get; private set; }
        public List<AuditLog> Audits { get; } = [];
        public List<CheckIn> Active { get; } = [];
        public int TodayCount { get; set; }

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity is FaceData face) Face = face;
            if (entity is AuditLog audit) Audits.Add(audit);
            if (entity is CheckIn checkIn) { checkIn.Id = Active.Count + 100; Active.Add(checkIn); }
            if (entity is QRCode code) Qr.Add(code);
        }

        public List<FaceData> Faces { get; } = [];
        public List<QRCode> Qr { get; } = [];
        public List<MemberMembership> Memberships { get; } = [];
        public List<PTSession> PtSessions { get; } = [];

        public Task<IReadOnlyList<FaceData>> GetActiveFaceDataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FaceData>>(Faces);
        public Task<IReadOnlyList<QRCode>> GetActiveQrCodesAsync(string? userId, string? token, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QRCode>>(Qr.Where(q => q.IsActive && (userId == null || q.UserId == userId) && (token == null || q.QRToken == token)).ToList());
        public Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationUser?>(new ApplicationUser { UserId = userId, FullName = "Member " + userId });
        public Task<IReadOnlyList<MemberMembership>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemberMembership>>(Memberships.Where(m => m.MemberUserId == userId).ToList());
        public Task<CheckIn?> GetCheckInAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.FirstOrDefault(c => c.Id == id));
        public Task<CheckIn?> GetOpenCheckInAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.FirstOrDefault(c => c.MemberUserId == userId && c.Status == CheckInStatus.Active));
        public Task<DateTime?> GetLastCheckInTimeAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.Where(c => c.MemberUserId == userId).Select(c => (DateTime?)c.CheckinTime).Max());
        public Task<IReadOnlyList<PTSession>> GetScheduledPtSessionsAsync(string userId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PTSession>>(PtSessions.Where(p => p.MemberUserId == userId && p.SessionDate == date && p.SessionStatus == PTSessionStatus.Scheduled).ToList());
        public Task<IReadOnlyList<(ApplicationUser User, IReadOnlyList<MemberMembership> Memberships, bool IsCheckedIn)>> SearchMembersAsync(
            string term, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(ApplicationUser, IReadOnlyList<MemberMembership>, bool)>>(Memberships
                .Select(m => m.MemberUserId).Distinct().Where(id => id.Contains(term))
                .Select(id => (new ApplicationUser { UserId = id, FullName = "Member " + id },
                    (IReadOnlyList<MemberMembership>)Memberships.Where(m => m.MemberUserId == id).ToList(),
                    Active.Any(c => c.MemberUserId == id && c.Status == CheckInStatus.Active)))
                .ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<FaceData?> GetFaceDataAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Face?.UserId == userId ? Face : null);
        public Task<IReadOnlyList<CheckIn>> GetActiveCheckInsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CheckIn>>(Active);
        public Task<int> CountCheckInsSinceAsync(DateTime utcFrom, CancellationToken cancellationToken = default) =>
            Task.FromResult(TodayCount);
    }
}
