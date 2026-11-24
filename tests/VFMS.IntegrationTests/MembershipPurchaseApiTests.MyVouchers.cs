using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private async Task<PromotionResponse> CreateWalletCampaignAsync(string code, PromotionAudience audience, decimal minimum = 0)
    {
        using var manager = Client("manager", "Manager");
        var now = _clock.Now.UtcDateTime;
        using var response = await manager.PostAsJsonAsync("/api/manager/promotions",
            new CreatePromotionRequest(code, code, PromotionDiscountType.Percentage, code == "NEW15" ? 15 : 10, minimum,
                100000, 100, now.AddDays(-1), now.AddDays(5), true)
            {
                Audience = audience, MaxRedemptionsPerUser = 1,
                NewCustomerDuration = audience == PromotionAudience.NewCustomers ? 1 : null,
                NewCustomerUnit = audience == PromotionAudience.NewCustomers ? PromotionDurationUnit.Months : null
            });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PromotionResponse>())!;
    }

    [Theory]
    [InlineData("PUBLIC10", PromotionAudience.AllCustomers)]
    [InlineData("NEW15", PromotionAudience.NewCustomers)]
    public async Task MyVoucherManagerCampaignSettlementAndAccountLimit(string code, PromotionAudience audience)
    {
        await CreateWalletCampaignAsync(code, audience);
        using var member = Client("member", "Member");
        using var other = Client("buyer", "Customer");
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.ApplicationUsers.SingleAsync(x => x.UserId == "member")).CreatedAt = _clock.Now.AddDays(-1).UtcDateTime;
            (await db.ApplicationUsers.SingleAsync(x => x.UserId == "buyer")).CreatedAt = _clock.Now.AddMonths(-2).UtcDateTime;
            await db.SaveChangesAsync();
        }
        var row = Assert.Single((await member.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        Assert.True(row.ExpiringSoon);
        Assert.Equal(1, row.RemainingUses);
        var order = await CreateAsync(member, Purchase([new(_basic)]));
        var held = await ApplyCheckoutVoucherAsync(member, order, code);
        Assert.Equal(MyVoucherStatus.Reserved, Assert.Single((await member.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!).Status);
        using var paid = await NotifyAsync(held, id: 24203);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        row = Assert.Single((await member.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        Assert.Equal(MyVoucherStatus.Exhausted, row.Status);
        Assert.Equal(0, row.RemainingUses);
        Assert.Equal(order.OrderNumber, Assert.Single(row.Uses).OrderNumber);
        var next = await CreateAsync(member, Purchase([new(_combo)]));
        using var again = await member.PostAsJsonAsync(VoucherRoute(next), new { voucherCode = code });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var otherOrder = await CreateAsync(other, Purchase([new(_basic)]));
        var othersWallet = (await other.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers?userId=member"))!;
        using var tryOther = await other.PostAsJsonAsync(VoucherRoute(otherOrder), new { voucherCode = code });
        if (audience == PromotionAudience.NewCustomers)
        {
            Assert.Empty(othersWallet);
            Assert.Contains("VOUCHER_ACCOUNT_AGE", await tryOther.Content.ReadAsStringAsync());
        }
        else
        {
            Assert.Empty(Assert.Single(othersWallet).Uses);
            Assert.Equal(HttpStatusCode.OK, tryOther.StatusCode);
        }
    }

    [Fact]
    public async Task MyVoucherRewardExpiryReleasesItAndPickerProtectsOrderAndMinimum()
    {
        var campaign = await CreateWalletCampaignAsync("REWARD", PromotionAudience.AssignedUsers, 600000);
        await SeedPersonalGrant(campaign.Id, "member");
        using var winner = Client("member", "Member");
        using var other = Client("buyer", "Customer");
        using var anon = Client(null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/my-vouchers")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers?userId=member"))!);
        var small = await CreateAsync(winner, Purchase([new(_basic)]));
        var url = $"/api/my-vouchers?orderNumber={small.OrderNumber}";
        Assert.Contains("600.000", Assert.Single((await winner.GetFromJsonAsync<MyVoucherResponse[]>(url))!).CheckoutIssue);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
        var order = await CreateAsync(winner, Purchase([new(_combo)]));
        Assert.Null(Assert.Single((await winner.GetFromJsonAsync<MyVoucherResponse[]>($"/api/my-vouchers?orderNumber={order.OrderNumber}"))!).CheckoutIssue);
        await ApplyCheckoutVoucherAsync(winner, order, "REWARD");
        _clock.Now = order.ExpiresAt;
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        var row = Assert.Single((await winner.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        Assert.Equal(MyVoucherStatus.Available, row.Status);
        Assert.Equal(1, row.RemainingUses);
        Assert.Equal(1, Assert.Single(row.Awards).Rank);
        Assert.Empty(row.Uses);
        _clock.Now = new DateTimeOffset(campaign.EndAt, TimeSpan.Zero);
        Assert.Equal(MyVoucherStatus.Expired, Assert.Single((await winner.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!).Status);
    }

    [SqlServerMigrationFact]
    public async Task MyVoucherSqlServerNewCustomerMemberAccountLoadsEligibleVoucher()
    {
        await using var database = await LegacySchemaMigrationTests.DisposableMigrationDatabase.CreateAsync();
        await _app.DisposeAsync();
        await StartApplicationAsync(database.ConnectionString);

        _clock.Now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.FromHours(7));

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var memberUser = await db.ApplicationUsers.SingleAsync(u => u.UserId == "member");
            memberUser.CreatedAt = new DateTime(2026, 11, 5, 7, 29, 21, DateTimeKind.Utc);

            var voucher = new Voucher
            {
                Code = "NEWMEMBER",
                Name = "Khách hàng mới",
                Audience = VoucherAudience.NewCustomers,
                NewCustomerDuration = 1,
                NewCustomerUnit = FreezeTierUnit.Months,
                DiscountType = DiscountType.PERCENT,
                DiscountValue = 15,
                MaxDiscountAmount = 100000,
                MinimumSpend = 0,
                StartAt = _clock.Now.UtcDateTime.AddDays(-10),
                EndAt = _clock.Now.UtcDateTime.AddDays(10),
                MaxRedemptions = 100,
                MaxRedemptionsPerUser = 1,
                VoucherStatus = VoucherStatus.Active,
                CreatedByUserId = "manager"
            };
            db.Vouchers.Add(voucher);
            await db.SaveChangesAsync();
        }

        using var member = Client("member", "Member");
        var response = await member.GetAsync("/api/my-vouchers");
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status: {response.StatusCode}, Body: {content}");
        var vouchers = (await response.Content.ReadFromJsonAsync<MyVoucherResponse[]>())!;
        Assert.Single(vouchers);
        Assert.Equal(MyVoucherStatus.Available, vouchers[0].Status);
        Assert.Equal("NEWMEMBER", vouchers[0].Code);

        // Verify that an account within the configured new-customer eligibility period can see and redeem
        // an eligible voucher even after becoming a Member.
        var order = await CreateAsync(member, Purchase([new(_basic)]));
        var applied = await ApplyCheckoutVoucherAsync(member, order, "NEWMEMBER");
        Assert.Equal(425000m, applied.AmountDue);
        Assert.Equal("NEWMEMBER", applied.AppliedVoucher!.Code);
    }
}
