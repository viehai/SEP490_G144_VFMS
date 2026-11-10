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
        db.AddRange(gym,
            new ApplicationUser { UserId = "m1", Gym = gym, Email = "m1@example.com", FullName = "An Member", PasswordHash = "x", AccountStatus = AccountStatus.Active },
            new ApplicationUser { UserId = "staff", Gym = gym, Email = "s@example.com", FullName = "Staff", PasswordHash = "x", AccountStatus = AccountStatus.Active },
            TestSeed.Membership("m1"));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static (CheckInService Service, MemberQrService Qr) Services(VfmsDbContext db)
    {
        var repository = new CheckInRepository(db);
        var qr = new MemberQrService(repository, TimeProvider.System);
        return (new CheckInService(repository, new NoFaces(), qr, TimeProvider.System), qr);
    }

    [Fact]
    public async Task KioskQrCheckInSavesVisitUsesTokenAndBlocksReEntry()   // SCRUM-44
    {
        await using var db = new SqliteTestContext(_options);
        var (service, qr) = Services(db);
        var token = (await qr.IssueAsync("m1")).Data!.Token;

        var result = await service.KioskCheckInAsync(new KioskScanRequest(null, token));
        var replay = await service.KioskCheckInAsync(new KioskScanRequest(null, token));
        var again = await service.KioskCheckInAsync(new KioskScanRequest(null, (await qr.IssueAsync("m1")).Data!.Token));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var visit = await db.CheckIns.AsNoTracking().SingleAsync();
        Assert.Equal((CheckInMethod.QRCode, CheckInStatus.Active), (visit.CheckinMethod, visit.Status));
        Assert.NotNull(visit.MemberMembershipId);
        Assert.Equal(403, replay.StatusCode);              // the token was used up
        Assert.Equal("ANTI_PASSBACK", again.ErrorCode);

        var checkedOut = await service.KioskCheckOutAsync(new KioskScanRequest(null, (await qr.IssueAsync("m1")).Data!.Token));   // SCRUM-45
        Assert.True(checkedOut.IsSuccess, checkedOut.ErrorMessage);
        var closed = await db.CheckIns.AsNoTracking().SingleAsync();
        Assert.Equal((CheckInStatus.CheckedOut, CheckInMethod.QRCode), (closed.Status, closed.CheckoutMethod!.Value));
        Assert.Equal("An Member", checkedOut.Data!.MemberName);
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
