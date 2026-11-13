using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Admin", HttpStatusCode.Forbidden)]
    public async Task VoucherApplyRequiresCustomerOrMemberRole(string? user, string? role, HttpStatusCode expected)
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        using var actor = Client(user, role);
        using var apply = await actor.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = "SAVE20" });
        Assert.Equal(expected, apply.StatusCode);
        await AssertUndiscountedPendingAsync(order);
    }

    [Theory]
    [InlineData("buyer", "Customer")]
    [InlineData("member", "Member")]
    public async Task VoucherOwnerAppliesPercentageAndReadsAuthoritativeDiscountedSummary(string user, string role)
    {
        using var purchaser = Client(user, role);
        var order = await CreateAsync(purchaser, Purchase([new(_basic), new(_combo)]));
        var voucher = await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(purchaser, order, "  save20  ");
        Assert.Equal(1300000m, applied.Subtotal);
        Assert.Equal(100000m, applied.DiscountAmount);
        Assert.Equal(1200000m, applied.AmountDue);
        Assert.NotNull(applied.AppliedVoucher);
        Assert.NotNull(applied.VietQr);
        Assert.Contains("amount=1200000", applied.VietQr.ImageUrl);
        Assert.Equal(order.Items.ToArray(), applied.Items.ToArray());
        var reloaded = await ReadAsync(await purchaser.GetAsync($"{Route}/{order.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal(applied.DiscountAmount, reloaded.DiscountAmount);
        Assert.Equal(applied.AppliedVoucher, reloaded.AppliedVoucher);
        Assert.Equal(applied.AmountDue, reloaded.AmountDue);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var redemption = Assert.Single(await db.VoucherRedemptions.ToListAsync());
        var stored = await db.Orders.Include(row => row.PaymentTransaction).SingleAsync();
        Assert.Equal(voucher.Id, redemption.VoucherId);
        Assert.Equal(stored.Id, redemption.OrderId);
        Assert.Equal(VoucherRedemptionStatus.Reserved, redemption.RedemptionStatus);
        Assert.Equal(100000m, redemption.DiscountAmount);
        Assert.Equal(1200000m, stored.PaymentTransaction!.AmountDue);
        Assert.Equal(PaymentStatus.Pending, stored.PaymentTransaction.PaymentStatus);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Empty(await db.MemberMemberships.ToListAsync());
        using var manager = Client("manager", "Manager");
        var administration = await manager.GetFromJsonAsync<PromotionResponse>($"/api/manager/promotions/{voucher.Id}");
        Assert.Equal(0, administration!.UsedCount);
        Assert.Equal(PromotionStatus.Active, administration.Status);
    }

    [Fact]
    public async Task VoucherApplyCannotTrustUserOrFinancialFieldsInRequest()
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 75000);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Packages.SingleAsync(row => row.Id == _basic)).Price = 900000;
            await db.SaveChangesAsync();
        }
        var applied = await ReadAsync(await purchaser.PostAsJsonAsync(VoucherRoute(order), new
        {
            voucherCode = "SAVE20", userId = "member", purchaserUserId = "member", subtotal = 1,
            clientSubtotal = 1, discountAmount = 500000, finalTotal = -1, amountDue = 0,
            usedCount = 10, redemptionStatus = "Confirmed", paymentStatus = "Paid"
        }), HttpStatusCode.OK);
        Assert.Equal(500000m, applied.Subtotal);
        Assert.Equal(75000m, applied.DiscountAmount);
        Assert.Equal(425000m, applied.AmountDue);
        Assert.Equal("Pending", applied.PaymentStatus);
        await using var verify = _app.Services.CreateAsyncScope();
        var store = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal("buyer", (await store.Orders.SingleAsync()).PurchaserUserId);
        Assert.Equal(VoucherRedemptionStatus.Reserved, (await store.VoucherRedemptions.SingleAsync()).RedemptionStatus);
    }

    [Fact]
    public async Task VoucherDifferentPurchaserCannotChangeAnotherPendingOrder()
    {
        using var purchaser = Client("buyer", "Customer");
        using var stranger = Client("member", "Member");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        using var response = await stranger.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = "SAVE20", userId = "buyer" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUndiscountedPendingAsync(order);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task VoucherInvalidCodeShapeReturnsValidationWithoutMutation(string? code)
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        using var response = await purchaser.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = code });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await AssertUndiscountedPendingAsync(order);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("scheduled")]
    [InlineData("expired")]
    [InlineData("end-boundary")]
    [InlineData("quota")]
    [InlineData("minimum")]
    [InlineData("deleted")]
    public async Task VoucherBusinessFailuresLeaveFinancialAndReservationStateUnchanged(string scenario)
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        if (scenario != "missing")
        {
            var voucher = await SeedCheckoutVoucherAsync();
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var stored = await db.Vouchers.SingleAsync(row => row.Id == voucher.Id);
            if (scenario == "disabled") stored.VoucherStatus = VoucherStatus.Inactive;
            if (scenario == "scheduled") stored.StartAt = _clock.Now.UtcDateTime.AddSeconds(1);
            if (scenario == "expired") stored.EndAt = _clock.Now.UtcDateTime.AddSeconds(-1);
            if (scenario == "end-boundary") stored.EndAt = _clock.Now.UtcDateTime;
            if (scenario == "minimum") stored.MinimumSpend = 500001;
            if (scenario == "deleted") stored.IsDeleted = true;
            if (scenario == "quota")
            {
                stored.MaxRedemptions = 1;
                db.VoucherRedemptions.Add(new VoucherRedemption
                {
                    VoucherId = voucher.Id, RedemptionStatus = VoucherRedemptionStatus.Confirmed,
                    DiscountAmount = 100000, RedeemedAt = _clock.Now.UtcDateTime, IsDeleted = true,
                    Order = new Order { OrderNumber = $"HISTORY-{Guid.NewGuid():N}", PurchaserUserId = "member", OrderStatus = OrderStatus.Paid }
                });
            }
            await db.SaveChangesAsync();
        }
        using var response = await purchaser.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = "SAVE20" });
        Assert.Equal(scenario == "quota" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var expectedCode = scenario switch
        {
            "missing" or "deleted" => "VOUCHER_NOT_FOUND",
            "disabled" => "VOUCHER_DISABLED",
            "scheduled" => "VOUCHER_SCHEDULED",
            "expired" or "end-boundary" => "VOUCHER_EXPIRED",
            "quota" => "VOUCHER_QUOTA_EXHAUSTED",
            _ => "MSG-22"
        };
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
        await AssertUndiscountedPendingAsync(order);
    }

    [Theory]
    [InlineData("Fixed", 75000, 100000, 75000)]
    [InlineData("Fixed", 600000, 100000, 500000)]
    [InlineData("Percentage", 10, 100000, 50000)]
    [InlineData("Percentage", 30, 100000, 100000)]
    [InlineData("Percentage", 100, 1000000, 500000)]
    [InlineData("Percentage", 12.34, 100000, 61700)]
    [InlineData("Percentage", 30, 100000.75, 100000)]
    public async Task VoucherCalculatesFixedPercentageCapAndFinalFloor(string type, decimal value, decimal cap, decimal discount)
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(discountType: type == "Fixed" ? DiscountType.FIXED : DiscountType.PERCENT,
            discountValue: value, cap: cap);
        var applied = await ApplyCheckoutVoucherAsync(purchaser, order);
        Assert.Equal(discount, applied.DiscountAmount);
        Assert.Equal(500000m - discount, applied.AmountDue);
        Assert.InRange(applied.AmountDue, 0, applied.Subtotal);
    }

    [Fact]
    public async Task VoucherMinimumSpendExactBoundaryUsesSubtotalBeforeDiscount()
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 100000, minimumSpend: 500000);
        var applied = await ApplyCheckoutVoucherAsync(purchaser, order);
        Assert.Equal(400000m, applied.AmountDue);
        Assert.True(applied.AmountDue < 500000);
    }

    [Fact]
    public async Task VoucherStartBoundaryIsInclusiveAndApplyIsIdempotentWithoutStacking()
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(startAt: _clock.Now.UtcDateTime);
        await SeedCheckoutVoucherAsync(code: "OTHER");
        var first = await ApplyCheckoutVoucherAsync(purchaser, order);
        var repeated = await ApplyCheckoutVoucherAsync(purchaser, order, " save20 ");
        Assert.Equal(first.AmountDue, repeated.AmountDue);
        Assert.Equal(first.AppliedVoucher, repeated.AppliedVoucher);
        using var stacked = await purchaser.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = "OTHER" });
        Assert.Equal(HttpStatusCode.Conflict, stacked.StatusCode);
        Assert.Equal("VOUCHER_ALREADY_APPLIED", (await stacked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Single(await db.VoucherRedemptions.ToListAsync());
        Assert.Equal(first.AmountDue, (await db.PaymentTransactions.SingleAsync()).AmountDue);
    }

    [Fact]
    public async Task VoucherLiveReservationProtectsQuotaWhileManagerUsageRemainsZero()
    {
        using var buyer = Client("buyer", "Customer");
        using var member = Client("member", "Member");
        var first = await CreateAsync(buyer, Purchase([new(_basic)]));
        var second = await CreateAsync(member, Purchase([new(_basic)]));
        var voucher = await SeedCheckoutVoucherAsync(quota: 1);
        await ApplyCheckoutVoucherAsync(buyer, first);
        using var unavailable = await member.PostAsJsonAsync(VoucherRoute(second), new { voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        Assert.Equal("VOUCHER_CAPACITY_UNAVAILABLE", (await unavailable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var manager = Client("manager", "Manager");
        var summary = await manager.GetFromJsonAsync<PromotionResponse>($"/api/manager/promotions/{voucher.Id}");
        Assert.Equal(0, summary!.UsedCount);
        Assert.Equal(PromotionStatus.Active, summary.Status);
        await AssertUndiscountedPendingAsync(second);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Expired")]
    [InlineData("Cancelled")]
    [InlineData("Empty")]
    public async Task VoucherRequiresNonemptyCurrentlyPendingCheckout(string state)
    {
        using var purchaser = Client("buyer", "Customer");
        var order = await CreateAsync(purchaser, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        if (state == "Paid")
            Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(order, id: 19001)).StatusCode);
        else if (state == "Expired")
            _clock.Now = order.ExpiresAt;
        else
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            if (state == "Empty") db.OrderItems.RemoveRange(await db.OrderItems.ToListAsync());
            else (await db.Orders.SingleAsync()).OrderStatus = OrderStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        using var response = await purchaser.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = "SAVE20" });
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
        await using var verify = _app.Services.CreateAsyncScope();
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<VfmsDbContext>().VoucherRedemptions.ToListAsync());
    }

    private static string VoucherRoute(MembershipPurchaseResponse order) => $"{Route}/{order.OrderNumber}/voucher";

    private static async Task<MembershipPurchaseResponse> ApplyCheckoutVoucherAsync(
        HttpClient purchaser, MembershipPurchaseResponse order, string code = "SAVE20") =>
        await ReadAsync(await purchaser.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = code }), HttpStatusCode.OK);

    private async Task<Voucher> SeedCheckoutVoucherAsync(string code = "SAVE20", DiscountType discountType = DiscountType.PERCENT,
        decimal discountValue = 20, decimal cap = 100000, decimal? minimumSpend = null, int? quota = 20,
        DateTime? startAt = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var voucher = new Voucher
        {
            Name = "Checkout campaign", Code = code, DiscountType = discountType, DiscountValue = discountValue,
            MaxDiscountAmount = discountType == DiscountType.PERCENT ? cap : null, MinimumSpend = minimumSpend,
            MaxRedemptions = quota, StartAt = startAt ?? _clock.Now.UtcDateTime.AddDays(-1),
            EndAt = _clock.Now.UtcDateTime.AddDays(1), CreatedByUserId = "manager", VoucherStatus = VoucherStatus.Active
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();
        return voucher;
    }

    private async Task AssertUndiscountedPendingAsync(MembershipPurchaseResponse order)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.Orders.AsNoTracking().Include(row => row.PaymentTransaction)
            .SingleAsync(row => row.OrderNumber == order.OrderNumber);
        Assert.Equal(order.Subtotal, stored.PaymentTransaction!.AmountDue);
        Assert.Equal(PaymentStatus.Pending, stored.PaymentTransaction.PaymentStatus);
        Assert.False(await db.VoucherRedemptions.AnyAsync(row => row.OrderId == stored.Id));
    }
}
