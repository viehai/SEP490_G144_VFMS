using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task LockerEmptyZoneCanBeDeletedAndNonEmptyZonesStayProtected()
    {
        await PrepareLocker();
        using var manager = Client("manager", "Manager");
        using var staff = Client("staff", "Staff");
        var zone = await (await manager.PostAsJsonAsync("/api/lockers/manager/zones", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var id = zone.GetProperty("id").GetInt32();
        using var forbidden = await staff.DeleteAsync($"/api/lockers/manager/zones/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var deleted = await manager.DeleteAsync($"/api/lockers/manager/zones/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var catalog = await manager.GetFromJsonAsync<JsonElement>("/api/lockers/manager/catalog");
        Assert.DoesNotContain(catalog.GetProperty("zones").EnumerateArray(), z => z.GetProperty("id").GetInt32() == id);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.True((await db.LockerZones.IgnoreQueryFilters().SingleAsync(z => z.Id == id)).IsDeleted);
        }
        using var missing = await manager.DeleteAsync($"/api/lockers/manager/zones/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var occupiedZone = catalog.GetProperty("zones")[0].GetProperty("id").GetInt32();
        using var blocked = await manager.DeleteAsync($"/api/lockers/manager/zones/{occupiedZone}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task LockerZoneWithOnlyInactiveLockersCannotBeDeleted()
    {
        var (lockerId, _) = await PrepareLocker();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Lockers.SingleAsync(l => l.Id == lockerId)).LockerStatus = VFMS.Domain.Enums.LockerStatus.Inactive;
            await db.SaveChangesAsync();
        }
        using var manager = Client("manager", "Manager");
        var catalog = await manager.GetFromJsonAsync<JsonElement>("/api/lockers/manager/catalog");
        var zone = catalog.GetProperty("zones")[0];
        Assert.Empty(zone.GetProperty("lockers").EnumerateArray());
        using var blocked = await manager.DeleteAsync($"/api/lockers/manager/zones/{zone.GetProperty("id").GetInt32()}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task LockerCustomTwoAndFiveMonthPricesKeepHeldTermsAndWorkForRenewal()
    {
        var (lockerId, _) = await PrepareLocker();
        using var manager = Client("manager", "Manager");
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        using var saved = await manager.PutAsJsonAsync("/api/lockers/manager/pricing", new
        {
            keyDeposit = 100_000, overdueDailyFee = 10_000,
            tiers = new[] { new { months = 2, rentalFee = 430_000 }, new { months = 5, rentalFee = 1_000_000 } }
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var prices = await saved.Content.ReadFromJsonAsync<JsonElement>();
        var two = Assert.Single(prices.EnumerateArray(), p => p.GetProperty("months").GetInt32() == 2);
        var checkout = await CreateOnlineLockerCheckout(member, new { lockerId, policyId = two.GetProperty("id").GetInt32() });
        Assert.Equal(530_000, checkout.GetProperty("amountDue").GetDecimal());

        using var edited = await manager.PutAsJsonAsync("/api/lockers/manager/pricing", new
        {
            keyDeposit = 120_000, overdueDailyFee = 15_000,
            tiers = new[] { new { months = 5, rentalFee = 1_100_000 } }
        });
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
        var five = Assert.Single((await edited.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(), p => p.GetProperty("months").GetInt32() == 5);
        var transfer = new DateTimeOffset(checkout.GetProperty("expiresAt").GetDateTime()).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(checkout.GetProperty("vietQr").GetProperty("transferContent").GetString()!, 530_000, 9711, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var rentalId = checkout.GetProperty("rentalId").GetInt32();
        using var handed = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{rentalId}/handover", LockerHandover);
        Assert.True(handed.IsSuccessStatusCode, await handed.Content.ReadAsStringAsync());
        var rental = await handed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, rental.GetProperty("termMonths").GetInt32());
        Assert.Equal(100_000, rental.GetProperty("keyDeposit").GetDecimal());
        var firstExpiry = rental.GetProperty("expiresAt").GetDateTime();
        Assert.Equal(Today.AddMonths(2), DateOnly.FromDateTime(firstExpiry.AddHours(7)));

        using var renewed = await member.PostAsJsonAsync($"/api/lockers/rentals/{rentalId}/renew", new { policyId = five.GetProperty("id").GetInt32() });
        Assert.True(renewed.IsSuccessStatusCode, await renewed.Content.ReadAsStringAsync());
        var renewal = await renewed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1_100_000, renewal.GetProperty("amountDue").GetDecimal());
        transfer = new DateTimeOffset(renewal.GetProperty("expiresAt").GetDateTime()).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var renewedPaid = await NotifyTransferAsync(renewal.GetProperty("vietQr").GetProperty("transferContent").GetString()!, 1_100_000, 9712, transfer);
        Assert.Equal(HttpStatusCode.OK, renewedPaid.StatusCode);
        rental = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.Equal(5, rental.GetProperty("termMonths").GetInt32());
        Assert.Equal(DateOnly.FromDateTime(firstExpiry.AddHours(7)).AddMonths(5), DateOnly.FromDateTime(rental.GetProperty("expiresAt").GetDateTime().AddHours(7)));
        Assert.Equal(100_000, rental.GetProperty("keyDeposit").GetDecimal());
    }

    [Fact]
    public async Task LockerZoneNamesAreAutomaticAndIgnoreLegacyNameAndFloor()
    {
        await PrepareLocker();
        using var manager = Client("manager", "Manager");
        using var first = await manager.PostAsJsonAsync("/api/lockers/manager/zones", new { name = "Tên tự nhập", floor = 77 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var zone = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("B", zone.GetProperty("name").GetString());
        Assert.Equal(0, zone.GetProperty("floor").GetInt32());
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            db.LockerZones.AddRange(Enumerable.Range('C', 24).Select(c => new LockerZone { GymId = gym.Id, ZoneName = ((char)c).ToString() }));
            await db.SaveChangesAsync();
        }
        using var next = await manager.PostAsJsonAsync("/api/lockers/manager/zones", new { });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        Assert.Equal("AA", (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(0, 2, 430000)]
    [InlineData(2, 2, 430000)]
    [InlineData(2, 5, 0)]
    public async Task LockerCustomPricesRejectInvalidOrDuplicateTerms(int firstMonths, int secondMonths, decimal fee)
    {
        using var manager = Client("manager", "Manager");
        using var response = await manager.PutAsJsonAsync("/api/lockers/manager/pricing", new
        {
            keyDeposit = 100_000, overdueDailyFee = 10_000,
            tiers = new[] { new { months = firstMonths, rentalFee = fee }, new { months = secondMonths, rentalFee = fee } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
