using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.CheckIn;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>Runs gym check-in against the real EF model (memberships, QR codes, the one-open-visit index).</summary>
public sealed class CheckInPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new SqliteTestContext(_options);
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Check-in test gym" };
        var member = new ApplicationUser { UserId = "m1", Gym = gym, Email = "m1@example.com", FullName = "An Member", PhoneNumber = "0901234567", PasswordHash = "x", AccountStatus = AccountStatus.Active };
        var staff = new ApplicationUser { UserId = "staff", Gym = gym, Email = "s@example.com", FullName = "Staff", PhoneNumber = "0907654321", PasswordHash = "x", AccountStatus = AccountStatus.Active };
        db.AddRange(gym, member, staff, TestSeed.Membership("m1"),
            new ApplicationUserRole { User = member, Role = new ApplicationRole { RoleName = "Member" } },
            new ApplicationUserRole { User = staff, Role = new ApplicationRole { RoleName = "Staff" } });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static (CheckInService Service, MemberQrService Qr) Services(VfmsDbContext db)
    {
        var repository = new CheckInRepository(db);
        var qr = new MemberQrService(repository, TimeProvider.System);
        return (new CheckInService(repository, new MembershipAccessRepository(db), new NoFaces(), qr, TimeProvider.System), qr);
    }

    [Fact]
    public async Task KioskQrCheckInSavesVisitKeepsDayCodeAndBlocksSecondOpenVisit()   // SCRUM-44
    {
        await using var db = new SqliteTestContext(_options);
        var (service, qr) = Services(db);
        var token = (await qr.GetOrIssueAsync("m1")).Data!.Token;
        var scan = new KioskScanRequest(null, token);   // one code all day: in, out and back in

        var result = await service.KioskCheckInAsync(scan);
        var again = await service.KioskCheckInAsync(scan);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var visit = await db.CheckIns.AsNoTracking().SingleAsync();
        Assert.Equal((CheckInMethod.QRCode, CheckInStatus.Active), (visit.CheckinMethod, visit.Status));
        Assert.NotNull(visit.MemberMembershipId);
        Assert.Equal("ALREADY_CHECKED_IN", again.ErrorCode);   // still inside; the code itself still works
        Assert.Equal(token, (await qr.GetOrIssueAsync("m1")).Data!.Token);   // saved, so reopening /my-qr shows it again

        var checkedOut = await service.KioskCheckOutAsync(scan);   // SCRUM-45
        Assert.True(checkedOut.IsSuccess, checkedOut.ErrorMessage);
        var closed = await db.CheckIns.AsNoTracking().SingleAsync();
        Assert.Equal((CheckInStatus.CheckedOut, CheckInMethod.QRCode), (closed.Status, closed.CheckoutMethod!.Value));
        Assert.Equal("An Member", checkedOut.Data!.MemberName);

        var reEntry = await service.KioskCheckInAsync(scan);
        Assert.True(reEntry.IsSuccess, reEntry.ErrorMessage);   // right after check-out: no cooldown
    }

    [Fact]
    public async Task VisitOpenLongerThanSixHoursIsAutoClosedByTheNextCheckIn()   // JOB-04 shortcut vs the one-open-visit index
    {
        await using var db = new SqliteTestContext(_options);
        db.CheckIns.Add(new CheckIn
        {
            MemberUserId = "m1", CheckinTime = DateTime.UtcNow.AddHours(-7), CheckinMethod = CheckInMethod.QRCode, Status = CheckInStatus.Active
        });
        await db.SaveChangesAsync();
        var (service, _) = Services(db);

        var result = await service.ManualCheckInAsync("m1", "staff");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var visits = await db.CheckIns.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Status).ToListAsync();
        Assert.Equal([CheckInStatus.AutoClosed, CheckInStatus.Active], visits);
    }

    [Fact]
    public async Task FrontDeskFindsMemberByOrderNumberAndChecksInManually()   // SCRUM-46
    {
        await using var db = new SqliteTestContext(_options);
        var (service, _) = Services(db);
        var orderNumber = await db.MemberMemberships.Select(m => m.OrderItem.Order.OrderNumber).SingleAsync();

        var byOrder = await service.SearchMembersAsync(orderNumber);
        var byPhone = await service.SearchMembersAsync("0901");
        var staffNotListed = await service.SearchMembersAsync("Staff");
        var result = await service.ManualCheckInAsync("m1", "staff");

        Assert.Equal(("m1", "Active"), (byOrder.Data!.Single().MemberUserId, byOrder.Data!.Single().MembershipStatus));
        Assert.Equal("m1", byPhone.Data!.Single().MemberUserId);
        Assert.Equal(404, staffNotListed.StatusCode);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var visit = await db.CheckIns.AsNoTracking().SingleAsync();
        Assert.Equal((CheckInMethod.ManualStaff, "staff"), (visit.CheckinMethod, visit.HandledByUserId));
        var inside = await new ActiveCheckInService(new CheckInRepository(db), TimeProvider.System, peakThreshold: 100)
            .GetActiveAsync(orderNumber.ToLowerInvariant());   // Active Check-ins by membership (order) number
        Assert.Equal(visit.Id, Assert.Single(inside.Data!.Items).CheckInId);

        var checkedOut = await service.ManualCheckOutAsync(visit.Id, "staff");   // SCRUM-47
        Assert.True(checkedOut.IsSuccess, checkedOut.ErrorMessage);
        var closed = await db.CheckIns.AsNoTracking().SingleAsync();
        Assert.Equal((CheckInStatus.CheckedOut, CheckInMethod.ManualStaff, "staff"), (closed.Status, closed.CheckoutMethod!.Value, closed.CheckedOutByUserId));
    }

    [Fact]
    public async Task StandalonePtContractPermitsEntryAtEitherChannelAndExitAfterExpiry()
    {
        await using var db = new SqliteTestContext(_options);
        var gym = await db.MemberMemberships.SingleAsync();
        gym.IsDeleted = true;
        var contract = new PTPurchase
        {
            MemberUserId = "m1", PTUserId = "staff", Status = PTPurchaseStatus.Active,
            StartDate = gym.StartDate, EndDate = gym.EndDate, TotalSessions = 4, RemainingSessions = 4,
            Order = new Order { PurchaserUserId = "m1", OrderNumber = "PT-ONLY", OrderStatus = OrderStatus.Paid }
        };
        db.PTPurchases.Add(contract);
        await db.SaveChangesAsync();
        var (service, qr) = Services(db);
        var scan = new KioskScanRequest(null, (await qr.GetOrIssueAsync("m1")).Data!.Token);
        var member = Assert.Single((await service.SearchMembersAsync("PT-ONLY")).Data!);
        Assert.Equal(("m1", "Active", contract.EndDate), (member.MemberUserId, member.MembershipStatus, member.ValidThrough));

        var kiosk = await service.KioskScanAsync(scan);
        Assert.Equal("CheckIn", kiosk.Data!.Action);
        Assert.Equal(contract.EndDate, kiosk.Data.CheckIn!.ValidThrough);
        Assert.Null((await db.CheckIns.AsNoTracking().SingleAsync()).MemberMembershipId);
        Assert.True((await service.ManualCheckOutAsync(kiosk.Data.CheckIn.CheckInId, "staff")).IsSuccess);
        Assert.True((await service.ManualCheckInAsync("m1", "staff")).IsSuccess);

        contract.EndDate = contract.StartDate;
        await db.SaveChangesAsync();
        Assert.Equal("CheckOut", (await service.KioskScanAsync(scan)).Data!.Action);
        Assert.Equal("Expired", Assert.Single((await service.SearchMembersAsync("PT-ONLY")).Data!).MembershipStatus);
        Assert.Equal("ACCESS_DENIED", (await service.KioskScanAsync(scan)).ErrorCode);
        Assert.Equal("ACCESS_DENIED", (await service.ManualCheckInAsync("m1", "staff")).ErrorCode);
    }

    [Fact]
    public async Task PtMakeUpKeepsEntryActiveOnlyWhileTheContractStillHasACredit()
    {
        await using var db = new SqliteTestContext(_options);
        var gym = await db.MemberMemberships.SingleAsync();
        gym.MembershipStatus = MembershipStatus.Expired;
        var cancelled = new PTSession
        {
            MemberUserId = "m1", PTUserId = "staff", SessionDate = gym.StartDate, SessionStatus = PTSessionStatus.Cancelled,
            CancelledAt = DateTime.UtcNow.AddDays(-6)
        };
        var pt = new PTPurchase
        {
            MemberUserId = "m1", PTUserId = "staff", StartDate = gym.StartDate.AddDays(-10), EndDate = gym.StartDate,
            TotalSessions = 1, RemainingSessions = 1, Sessions = [cancelled]
        };
        db.PTPurchases.Add(pt);
        await db.SaveChangesAsync();

        var (service, _) = Services(db);
        var entry = await service.ManualCheckInAsync("m1", "staff");
        Assert.Equal(PersonalTrainingRules.MakeUpDeadline(pt.EndDate), entry.Data!.ValidThrough);
        cancelled.IsCreditForfeited = true;
        await db.SaveChangesAsync();
        Assert.True((await service.ManualCheckOutAsync(entry.Data.CheckInId, "staff")).IsSuccess);
        Assert.Equal("ACCESS_DENIED", (await service.ManualCheckInAsync("m1", "staff")).ErrorCode);
    }

    [Theory]
    [InlineData(PTPurchaseStatus.PendingPayment, false, false, false, "PendingPayment")]
    [InlineData(PTPurchaseStatus.Completed, false, false, false, "Completed")]
    [InlineData(PTPurchaseStatus.Frozen, false, false, false, "Frozen")]
    [InlineData(PTPurchaseStatus.Active, true, false, false, "Frozen")]
    [InlineData(PTPurchaseStatus.Active, false, true, false, "Scheduled")]
    [InlineData(PTPurchaseStatus.Active, false, false, true, "Completed")]
    public async Task LinkedPtComponentUsesItsContractsCurrentStatus(PTPurchaseStatus status, bool frozenToday,
        bool future, bool completed, string expected)
    {
        await using var db = new SqliteTestContext(_options);
        var plan = await db.MemberMemberships.Include(m => m.PackageComponent).SingleAsync();
        plan.PackageComponent.ComponentType = ComponentType.PT;
        var contract = new PTPurchase
        {
            MemberUserId = "m1", PTUserId = "staff", MemberMembershipId = plan.Id, Status = status,
            StartDate = future ? plan.EndDate : plan.StartDate, EndDate = plan.EndDate, IsCompleted = completed
        };
        if (frozenToday)
            contract.PTFreezes.Add(new PTFreeze { RequestedByUserId = "m1", ActualStartDate = plan.StartDate,
                ActualEndDate = plan.EndDate, RequestStatus = PTFreezeRequestStatus.Approved });
        db.PTPurchases.Add(contract);
        await db.SaveChangesAsync();

        var (service, qr) = Services(db);
        Assert.Equal(expected, Assert.Single((await service.SearchMembersAsync("0901")).Data!).MembershipStatus);
        var scan = new KioskScanRequest(null, (await qr.GetOrIssueAsync("m1")).Data!.Token);
        Assert.Equal("ACCESS_DENIED", (await service.KioskScanAsync(scan)).ErrorCode);
        Assert.Equal("ACCESS_DENIED", (await service.ManualCheckInAsync("m1", "staff")).ErrorCode);
        Assert.False(await db.CheckIns.AnyAsync());

        db.MemberMemberships.Add(TestSeed.Membership("m1", ComponentType.YOGA));
        await db.SaveChangesAsync();
        Assert.Equal("Active", Assert.Single((await service.SearchMembersAsync("0901")).Data!).MembershipStatus);
        Assert.True((await service.ManualCheckInAsync("m1", "staff")).IsSuccess);
    }

    [Fact]
    public async Task DatabaseRefusesASecondOpenVisit()
    {
        await using var db = new SqliteTestContext(_options);
        db.CheckIns.Add(new CheckIn { MemberUserId = "m1", CheckinTime = DateTime.UtcNow, Status = CheckInStatus.Active });
        db.CheckIns.Add(new CheckIn { MemberUserId = "m1", CheckinTime = DateTime.UtcNow, Status = CheckInStatus.CheckedOut });
        await db.SaveChangesAsync();

        db.CheckIns.Add(new CheckIn { MemberUserId = "m1", CheckinTime = DateTime.UtcNow, Status = CheckInStatus.Active });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class NoFaces : IFaceTemplateProtector
    {
        public byte[] Protect(IReadOnlyList<float> descriptor) => [];
        public float[] Unprotect(byte[] protectedTemplate) => [];
    }
}
