using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData("Minh An")]
    [InlineData("0901234567")]
    [InlineData("AN.SEARCH@VFMS.TEST")]
    public async Task LockerMemberSearchMatchesNamePhoneAndEmail(string search)
    {
        await PrepareLocker();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(u => u.UserId == "member");
            user.FullName = "Nguyễn Minh An";
            user.PhoneNumber = "0901234567";
            user.Email = "an.search@vfms.test";
            await db.SaveChangesAsync();
        }
        using var staff = Client("staff", "Staff");
        using var response = await staff.GetAsync($"/api/lockers/staff/members?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = Assert.Single(results.EnumerateArray());
        Assert.Equal("member", result.GetProperty("id").GetString());
        Assert.Equal("an.search@vfms.test", result.GetProperty("email").GetString());
        Assert.True(result.GetProperty("eligible").GetBoolean());

        using var outsider = Client("other-staff", "Staff");
        var otherGym = await outsider.GetFromJsonAsync<JsonElement>($"/api/lockers/staff/members?search={Uri.EscapeDataString(search)}");
        Assert.Empty(otherGym.EnumerateArray());
        using var member = Client("member", "Member");
        using var forbidden = await member.GetAsync("/api/lockers/staff/members?search=Minh");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Theory]
    [InlineData("0901234567", false)]
    [InlineData("AN.SEARCH@VFMS.TEST", false)]
    [InlineData("a01", false)]
    [InlineData("0901234567", true)]
    [InlineData("AN.SEARCH@VFMS.TEST", true)]
    public async Task LockerContractSearchMatchesContactInformation(string search, bool unpaid)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var member = Client("member", "Member");
        var checkout = await CreateOnlineLockerCheckout(member, new { lockerId, policyId });
        var transfer = new DateTimeOffset(checkout.GetProperty("expiresAt").GetDateTime()).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(checkout.GetProperty("vietQr").GetProperty("transferContent").GetString()!,
            checkout.GetProperty("amountDue").GetDecimal(), 9820, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(u => u.UserId == "member");
            user.PhoneNumber = "0901234567";
            user.Email = "an.search@vfms.test";
            if (unpaid)
            {
                var rental = await db.LockerRentalContracts.SingleAsync(r => r.Id == checkout.GetProperty("rentalId").GetInt32());
                rental.ContractStatus = VFMS.Domain.Enums.LockerContractStatus.Abandoned;
                rental.OutstandingAmount = 10_000;
            }
            await db.SaveChangesAsync();
        }
        using var staff = Client("staff", "Staff");
        var route = unpaid ? "unpaid" : "rentals";
        var matches = await staff.GetFromJsonAsync<JsonElement>($"/api/lockers/staff/{route}?search={Uri.EscapeDataString(search)}");
        Assert.Single(matches.EnumerateArray());
        var absent = await staff.GetFromJsonAsync<JsonElement>($"/api/lockers/staff/{route}?search=nobody@vfms.test");
        Assert.Empty(absent.EnumerateArray());
    }
}
