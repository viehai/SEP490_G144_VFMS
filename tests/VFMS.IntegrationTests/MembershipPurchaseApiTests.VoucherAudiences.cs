using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task VoucherNewCustomerDeadlineDoesNotInvalidateAPendingOrderButStopsFreshReservations()
    {
        using var buyer = Client("buyer", "Customer");
        var first = await CreateAsync(buyer, Purchase([new(_basic, "gift@example.test")]));
        var second = await CreateAsync(buyer, Purchase([new(_basic)]));
        var campaign = await SeedCheckoutVoucherAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var voucher = await db.Vouchers.SingleAsync(v => v.Id == campaign.Id);
            voucher.Audience = VoucherAudience.NewCustomers;
            voucher.NewCustomerDuration = 1;
            voucher.NewCustomerUnit = FreezeTierUnit.Days;
            var buyerRow = await db.ApplicationUsers.SingleAsync(x => x.UserId == "buyer");
            buyerRow.CreatedAt = _clock.Now.UtcDateTime.AddMinutes(1).AddDays(-1);
            await db.SaveChangesAsync();
        }
        var applied = await ApplyCheckoutVoucherAsync(buyer, first);
        _clock.Now = _clock.Now.AddMinutes(1);
        using var unavailable = await buyer.PostAsJsonAsync(VoucherRoute(second), new { voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.BadRequest, unavailable.StatusCode);
        Assert.Contains("VOUCHER_ACCOUNT_AGE", await unavailable.Content.ReadAsStringAsync());
        using var settled = await NotifyAsync(applied, id: 141104);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
    }

    [Fact]
    public async Task VoucherAccountLimitIsSharedAcrossYogaRenewalAndGiftPurchases()
    {
        var membership = await SeedMembershipAsync(Today.AddDays(5), MembershipStatus.Active);
        await SeedYogaClassAsync("staff", _clock.Now.UtcDateTime.AddDays(7));
        var yogaId = await SeedYogaPlanAsync();
        using var member = Client("member", "Member");
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var renewal = await ReadAsync(await member.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(membership.Id)), HttpStatusCode.Created);
        var yoga = await CreateAsync(member, Purchase([new(yogaId, "gift@example.test")], MembershipPaymentMethods.Cash));
        var campaign = await SeedCheckoutVoucherAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Vouchers.SingleAsync(v => v.Id == campaign.Id)).MaxRedemptionsPerUser = 1;
            await db.SaveChangesAsync();
        }
        await ApplyCheckoutVoucherAsync(member, renewal);
        using var held = await member.PostAsJsonAsync(VoucherRoute(yoga), new { voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        using var remove = await member.DeleteAsync(VoucherRoute(renewal));
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        var discounted = await ApplyCheckoutVoucherAsync(member, yoga);
        using var settled = await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{yoga.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(discounted.AmountDue));
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        using var spent = await member.PostAsJsonAsync(VoucherRoute(renewal), new { voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.Conflict, spent.StatusCode);
        var other = await CreateAsync(buyer, Purchase([new(_basic, "member@example.test")]));
        await ApplyCheckoutVoucherAsync(buyer, other);
    }

    [Fact]
    public async Task VoucherAssignedToMemberProtectsBothPTAndGymAndConfirmsNoChargeOnlyOnce()
    {
        var booking = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var buyer = Client("buyer", "Customer");
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", booking);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var pt = (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var campaign = await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: pt.AmountDue);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Vouchers.SingleAsync(v => v.Id == campaign.Id)).Audience = VoucherAudience.AssignedUsers;
            await db.SaveChangesAsync();
        }
        var ptPayment = $"/api/pt/purchases/{pt.OrderNumber}/payment";
        using var unauthorized = await member.PostAsJsonAsync(ptPayment, new PTPaymentRequest("SAVE20"));
        Assert.Equal(HttpStatusCode.Conflict, unauthorized.StatusCode);
        var grantId = await SeedPersonalGrant(campaign.Id, "member");
        var gift = await CreateAsync(buyer, Purchase([new(_basic, "member@example.test")]));
        using var stolen = await buyer.PostAsJsonAsync(VoucherRoute(gift), new { voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        using var paid = await member.PostAsJsonAsync(ptPayment, new PTPaymentRequest("SAVE20"));
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
        var result = (await paid.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        Assert.Equal("Paid", result.Status);
        Assert.Equal(0m, result.AmountDue);
        using var retry = await member.PostAsJsonAsync(ptPayment, new PTPaymentRequest("SAVE20"));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var wrongRoute = await member.PostAsync($"{Route}/{pt.OrderNumber}/complete-no-charge", null);
        Assert.Equal(HttpStatusCode.NotFound, wrongRoute.StatusCode);
        await using var verify = _app.Services.CreateAsyncScope();
        var data = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var row = await data.VoucherRedemptions.SingleAsync();
        Assert.Equal(grantId, row.VoucherGrantId);
        Assert.Equal(VoucherRedemptionStatus.Confirmed, row.RedemptionStatus);
        Assert.Equal(1, await data.Invoices.CountAsync());
        Assert.Equal(PaymentMethod.NoCharge, (await data.PaymentTransactions.SingleAsync(p => p.AmountDue == 0)).PaymentMethod);
        Assert.Equal(PTPurchaseStatus.Active, (await data.PTPurchases.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoucherPTNewCustomerChecksAccountAgeOnlyWhenAcquiringAReservation(bool expireHold)
    {
        var booking = await PreparePTCheckout();
        using var member = Client("member", "Member");
        var hold = await HoldPT(member, booking);
        var deadline = _clock.Now.AddMinutes(1);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var campaign = await db.Vouchers.SingleAsync(v => v.Code == "PT-OFF");
            campaign.Audience = VoucherAudience.NewCustomers;
            campaign.NewCustomerDuration = 1;
            campaign.NewCustomerUnit = FreezeTierUnit.Days;
            (await db.ApplicationUsers.SingleAsync(u => u.UserId == "member")).CreatedAt = deadline.AddDays(-1).UtcDateTime;
            await db.SaveChangesAsync();
        }
        var issued = await IssueQr(member, hold, "PT-OFF");
        var transferAt = new DateTimeOffset(issued.ExpiresAt, TimeSpan.Zero).AddMinutes(-13);
        _clock.Now = transferAt.AddSeconds(1);
        if (expireHold)
        {
            _clock.Now = new DateTimeOffset(issued.HoldExpiresAt, TimeSpan.Zero).AddSeconds(1);
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<VFMS.Application.Services.PersonalTrainingService>().ExpirePending(default);
        }
        using var callback = await NotifyTransferAsync(issued.VietQr!.TransferContent, issued.AmountDue, 141110, transferAt);
        Assert.True(callback.IsSuccessStatusCode, await callback.Content.ReadAsStringAsync());
        await using var verify = _app.Services.CreateAsyncScope();
        var data = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var row = await data.VoucherRedemptions.SingleAsync();
        Assert.Equal(expireHold ? VoucherRedemptionStatus.Released : VoucherRedemptionStatus.Confirmed, row.RedemptionStatus);
        Assert.Equal(expireHold ? 0 : 1, await data.Invoices.CountAsync());
        if (expireHold) Assert.Equal("Pending", (await data.PaymentTransactions.SingleAsync()).RefundStatus);
    }

    private async Task<int> SeedPersonalGrant(int voucherId, string userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var start = _clock.Now.UtcDateTime.AddDays(-4);
        var grant = new VoucherGrant
        {
            UserId = userId, VoucherId = voucherId, CompetitionPrize = new CompetitionPrize
            {
                PrizeName = "Giải nhất", Rank = 1, VoucherId = voucherId,
                Competition = new Competition
                {
                    CompetitionName = "Thử thách tháng", StartDate = start.AddDays(2), EndDate = start.AddDays(3),
                    RegistrationStart = start, RegistrationEnd = start.AddDays(1), CreatedByUserId = "manager"
                }
            }
        };
        db.Set<VoucherGrant>().Add(grant);
        await db.SaveChangesAsync();
        return grant.Id;
    }

    [SqlServerMigrationFact]
    public async Task VoucherSqlServerLastGlobalSlotAndGymRacingPTAccountLimit()
    {
        await using var database = await LegacySchemaMigrationTests.DisposableMigrationDatabase.CreateAsync();
        await _app.DisposeAsync();
        await StartApplicationAsync(database.ConnectionString);
        using var buyer = Client("buyer", "Customer");
        using var member = Client("member", "Member");
        var first = await CreateAsync(buyer, Purchase([new(_basic)]));
        var second = await CreateAsync(member, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(quota: 1);
        var last = await Task.WhenAll(buyer.PostAsJsonAsync(VoucherRoute(first), new { voucherCode = "SAVE20" }),
            member.PostAsJsonAsync(VoucherRoute(second), new { voucherCode = "SAVE20" }));
        await AssertSingleVoucherReservation(last);
        var booking = await PreparePTCheckout();
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", booking);
        Assert.True(hold.StatusCode == HttpStatusCode.Created, await hold.Content.ReadAsStringAsync());
        var pt = (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Vouchers.SingleAsync(v => v.Code == "PT-OFF")).MaxRedemptionsPerUser = 1;
            await db.SaveChangesAsync();
        }
        var gym = await CreateAsync(member, Purchase([new(_basic, "gift@example.test")]));
        var cross = await Task.WhenAll(member.PostAsJsonAsync(VoucherRoute(gym), new { voucherCode = "PT-OFF" }),
            member.PostAsJsonAsync($"/api/pt/purchases/{pt.OrderNumber}/payment", new PTPaymentRequest("PT-OFF")));
        await AssertSingleVoucherReservation(cross);
        await using var verify = _app.Services.CreateAsyncScope();
        var data = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(2, await data.VoucherRedemptions.CountAsync(r => r.RedemptionStatus == VoucherRedemptionStatus.Reserved));
    }

    private static async Task AssertSingleVoucherReservation(HttpResponseMessage[] responses)
    {
        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
        var details = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        foreach (var response in responses) response.Dispose();
        Assert.True(statuses.SequenceEqual([HttpStatusCode.OK, HttpStatusCode.Conflict]), string.Join(Environment.NewLine, details));
    }

    [SqlServerMigrationFact]
    public async Task VoucherSqlServerGrantReleaseReapplyAndPaymentAgainstExpiry()
    {
        await using var database = await LegacySchemaMigrationTests.DisposableMigrationDatabase.CreateAsync();
        await _app.DisposeAsync();
        await StartApplicationAsync(database.ConnectionString);
        using var buyer = Client("buyer", "Customer");
        var campaign = await SeedCheckoutVoucherAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Vouchers.SingleAsync(v => v.Id == campaign.Id)).Audience = VoucherAudience.AssignedUsers;
            await db.SaveChangesAsync();
        }
        var grant = await SeedPersonalGrant(campaign.Id, "buyer");
        var first = await CreateAsync(buyer, Purchase([new(_basic)]));
        var second = await CreateAsync(buyer, Purchase([new(_basic)]));
        await ApplyCheckoutVoucherAsync(buyer, first);
        using var denied = await buyer.PostAsJsonAsync(VoucherRoute(second), new { voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        using var removed = await buyer.DeleteAsync(VoucherRoute(first));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var applied = await ApplyCheckoutVoucherAsync(buyer, second);
        _clock.Now = second.ExpiresAt;
        var transfer = NotifyAsync(applied, id: 141105);
        await Task.WhenAll(transfer, RunJobAsync((svc, ct) => svc.ExpireOrdersAsync(ct)));
        using var callback = await transfer;
        Assert.Contains(callback.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        await using var verify = _app.Services.CreateAsyncScope();
        var data = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var order = await data.Orders.Include(o => o.VoucherRedemption).Include(o => o.PaymentTransaction)
            .SingleAsync(o => o.OrderNumber == second.OrderNumber);
        var paid = callback.IsSuccessStatusCode;
        Assert.Equal(paid ? OrderStatus.Paid : OrderStatus.Expired, order.OrderStatus);
        Assert.Equal(paid ? VoucherRedemptionStatus.Confirmed : VoucherRedemptionStatus.Released, order.VoucherRedemption!.RedemptionStatus);
        Assert.Equal(grant, order.VoucherRedemption.VoucherGrantId);
        Assert.Equal(paid ? 1 : 0, await data.Invoices.CountAsync());
        Assert.DoesNotContain(await data.VoucherRedemptions.ToListAsync(), r => r.RedemptionStatus == VoucherRedemptionStatus.Reserved);
    }
}
