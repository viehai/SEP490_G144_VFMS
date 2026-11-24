using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Competitions;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task CompetitionRewardGoesFromPublishedPrizeToPrivateWalletAndOnePaidOrder()
    {
        using var manager = Client("manager", "Manager");
        using var staff = Client("staff", "Staff");
        using var winner = Client("member", "Member");
        using var other = Client("buyer", "Customer");
        using var guest = Client();
        var now = _clock.Now.UtcDateTime;
        var promotion = new CreatePromotionRequest("Prix du champion", "CHAMPION30", PromotionDiscountType.Percentage, 30,
            null, 250000, 3, now.AddDays(-1), now.AddDays(10), true)
            { Audience = PromotionAudience.AssignedUsers, MaxRedemptionsPerUser = 1 };
        using var promotionResponse = await manager.PostAsJsonAsync("/api/manager/promotions", promotion);
        promotionResponse.EnsureSuccessStatusCode();
        var voucher = (await promotionResponse.Content.ReadFromJsonAsync<PromotionResponse>())!;
        var competition = new CompetitionRequest("Thử thách thể lực", now.AddDays(-1), now.AddMinutes(10),
            now.AddHours(1), now.AddDays(1), Prizes: [new("Vô địch", 1, VoucherId: voucher.Id)]);
        using var created = await manager.PostAsJsonAsync("/api/competitions", competition);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<CompetitionResponse>())!.Id;
        var url = $"/api/competitions/{id}";
        (await manager.PostAsync($"{url}/publish", null)).EnsureSuccessStatusCode();
        var publicResponse = await guest.GetAsync(url);
        publicResponse.EnsureSuccessStatusCode();
        var details = (await publicResponse.Content.ReadFromJsonAsync<CompetitionResponse>())!;
        Assert.Equal(30, Assert.Single(details.Prizes).Reward!.DiscountValue);
        Assert.DoesNotContain("CHAMPION30", await publicResponse.Content.ReadAsStringAsync());
        using var registered = await winner.PostAsync($"{url}/register", null);
        registered.EnsureSuccessStatusCode();
        var join = (await registered.Content.ReadFromJsonAsync<CompetitionParticipantResponse>())!;
        _clock.Now = _clock.Now.AddDays(1);
        (await manager.PostAsync($"{url}/complete", null)).EnsureSuccessStatusCode();
        (await staff.PostAsJsonAsync($"/api/competitions/participants/{join.Id}/result", new RecordCompetitionResultRequest(90, 1))).EnsureSuccessStatusCode();
        Assert.Empty((await winner.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"{url}/results/confirm-all", null)).StatusCode);
        (await manager.PostAsync($"{url}/results/confirm-all", null)).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync("/api/competitions/results/confirm", new ConfirmCompetitionResultRequest(join.Id))).EnsureSuccessStatusCode();
        var granted = Assert.Single((await winner.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        Assert.Equal(MyVoucherStatus.Available, granted.Status);
        Assert.Equal(1, granted.RemainingUses);
        Assert.Equal(1, Assert.Single(granted.Awards).Rank);
        Assert.Empty((await other.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        var otherOrder = await CreateAsync(other, Purchase([new(_basic)]));
        var denied = await other.PostAsJsonAsync(VoucherRoute(otherOrder), new { voucherCode = voucher.Code });
        Assert.False(denied.IsSuccessStatusCode);
        var order = await CreateAsync(winner, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        var applied = await ApplyCheckoutVoucherAsync(winner, order, voucher.Code);
        Assert.Equal(350000m, applied.AmountDue);
        (await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{order.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(applied.AmountDue))).EnsureSuccessStatusCode();
        var used = Assert.Single((await winner.GetFromJsonAsync<MyVoucherResponse[]>("/api/my-vouchers"))!);
        Assert.Equal(MyVoucherStatus.Exhausted, used.Status);
        Assert.Equal(0, used.RemainingUses);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var grant = await db.VoucherGrants.SingleAsync();
        Assert.Equal(grant.Id, (await db.VoucherRedemptions.SingleAsync()).VoucherGrantId);
        var notification = await db.Notifications.SingleAsync(n => n.NotificationType == nameof(NotificationType.CompetitionResult));
        Assert.Equal("member", notification.RecipientUserId);
        Assert.Contains(voucher.Name, notification.Message);
    }
}
