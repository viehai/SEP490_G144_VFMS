using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task ManagerLockerOperationsCanInspectAndMaintainButCannotCollectCashOrHandOverKeys()
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var manager = Client("manager", "Manager");
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        var maintenancePath = $"/api/lockers/staff/grid/{lockerId}/maintenance";
        using var maintenance = await manager.PutAsJsonAsync(maintenancePath, new { maintenance = true });
        Assert.Equal(HttpStatusCode.OK, maintenance.StatusCode);
        using var restored = await manager.PutAsJsonAsync(maintenancePath, new { maintenance = false, safeInspection = true });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var order = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var id = order.GetProperty("rentalId").GetInt32();
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/handover",
            new { termsAccepted = true, identityVerified = true, cashAmount = 350_000 });
        Assert.Equal(HttpStatusCode.OK, handover.StatusCode);
        var active = await handover.Content.ReadFromJsonAsync<JsonElement>();
        _clock.Now = new DateTimeOffset(active.GetProperty("expiresAt").GetDateTime()).AddDays(1);
        using var details = await manager.GetAsync($"/api/lockers/staff/rentals/{id}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var rental = (await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rental");
        Assert.Equal(id, rental.GetProperty("id").GetInt32());
        Assert.True(rental.GetProperty("overdueDays").GetInt32() > 0);
        using var registry = await manager.GetAsync("/api/lockers/staff/rentals?overdueOnly=true&search=member");
        Assert.Equal(HttpStatusCode.OK, registry.StatusCode);
        Assert.Single((await registry.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(
            $"/api/lockers/staff/orders/{order.GetProperty("orderNumber").GetString()}/cash", new { amount = 350_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(
            $"/api/lockers/staff/rentals/{id}/handover", LockerHandover)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/lockers/staff/rentals/{id}")).StatusCode);
    }
}
