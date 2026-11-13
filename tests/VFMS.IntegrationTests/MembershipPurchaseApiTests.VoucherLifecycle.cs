using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task VoucherPaymentConfirmsExactlyOnceAgainstDiscountedAmountAndSnapshotsSurviveManagerEdits()
    {
        using var buyer = Client("buyer", "Customer");
        var original = await CreateAsync(buyer, Purchase([new(_basic)]));
        var voucher = await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, original);
        Assert.Equal("SAVE20", applied.AppliedVoucher!.Code);
        Assert.Equal("Checkout campaign", applied.AppliedVoucher.Name);
        Assert.Equal("Reserved", applied.AppliedVoucher.RedemptionStatus);
        using var manager = Client("manager", "Manager");
        var changed = new UpdatePromotionRequest("Edited campaign", "CHANGED", PromotionDiscountType.Fixed,
            1m, 999999999m, null, 20, _clock.Now.UtcDateTime.AddDays(1), _clock.Now.UtcDateTime.AddDays(2), false);
        using var updated = await manager.PutAsJsonAsync($"/api/manager/promotions/{voucher.Id}", changed);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var same = await ApplyCheckoutVoucherAsync(buyer, original, "save20");
        Assert.Equal(applied.AppliedVoucher, same.AppliedVoucher);
        Assert.Equal(applied.AmountDue, same.AmountDue);

        using var wrongAmount = await NotifyAsync(applied, id: 19101, amount: original.Subtotal);
        Assert.Equal(HttpStatusCode.Conflict, wrongAmount.StatusCode);
        using var settled = await NotifyAsync(applied, id: 19102);
        using var duplicate = await NotifyAsync(applied, id: 19102);
        using var differentReference = await NotifyAsync(applied, id: 19103);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, differentReference.StatusCode);
        var paid = await ReadAsync(await buyer.GetAsync($"{Route}/{original.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);
        Assert.Equal(400000m, paid.AmountDue);
        Assert.Equal(100000m, paid.DiscountAmount);
        Assert.Equal("SAVE20", paid.AppliedVoucher!.Code);
        Assert.Equal("Confirmed", paid.AppliedVoucher.RedemptionStatus);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(VoucherRedemptionStatus.Confirmed, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        Assert.Equal(400000m, (await db.PaymentTransactions.SingleAsync()).AmountReceived);
        Assert.Single(await db.MemberMemberships.ToListAsync());
        Assert.Single(await db.Invoices.ToListAsync());
        Assert.Equal(1, (await manager.GetFromJsonAsync<PromotionResponse>($"/api/manager/promotions/{voucher.Id}"))!.UsedCount);
    }

    [Fact]
    public async Task VoucherCashSettlementUsesDiscountedReceiptAndAuditsPaymentOnce()
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var order = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 75000);
        var applied = await ApplyCheckoutVoucherAsync(buyer, order);
        var route = $"/api/staff/membership-purchases/{order.OrderNumber}/confirm-cash";
        using var fullPrice = await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(order.Subtotal));
        Assert.Equal(HttpStatusCode.Conflict, fullPrice.StatusCode);
        var paid = await ReadAsync(await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(applied.AmountDue)), HttpStatusCode.OK);
        using var repeated = await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(applied.AmountDue));
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal("Confirmed", paid.AppliedVoucher!.RedemptionStatus);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(VoucherRedemptionStatus.Confirmed, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        Assert.Equal(425000m, (await db.PaymentTransactions.SingleAsync()).AmountReceived);
        Assert.Single(await db.MemberMemberships.ToListAsync());
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(MembershipPaymentMethods.VietQr)]
    [InlineData(MembershipPaymentMethods.Cash)]
    public async Task VoucherNoChargeRequiresExplicitCompletionAndRemainsIdempotent(string method)
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)], method));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 600000);
        var applied = await ApplyCheckoutVoucherAsync(buyer, order);
        Assert.Equal(0, applied.AmountDue);
        Assert.Equal(500000m, applied.DiscountAmount);
        Assert.Equal("Pending", applied.OrderStatus);
        Assert.Null(applied.VietQr);
        Assert.Equal("Reserved", applied.AppliedVoucher!.RedemptionStatus);
        var route = $"{Route}/{order.OrderNumber}/complete-no-charge";
        var paid = await ReadAsync(await buyer.PostAsync(route, null), HttpStatusCode.OK);
        var repeated = await ReadAsync(await buyer.PostAsync(route, null), HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);
        Assert.Equal("NoCharge", paid.PaymentMethod);
        Assert.Equal("Confirmed", paid.AppliedVoucher!.RedemptionStatus);
        Assert.Equal(paid.InvoiceNumber, repeated.InvoiceNumber);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Single(await db.MemberMemberships.ToListAsync());
        Assert.Single(await db.Invoices.ToListAsync());
        Assert.Equal(0, (await db.PaymentTransactions.SingleAsync()).AmountReceived);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    [InlineData("member", "Member", HttpStatusCode.NotFound)]
    public async Task VoucherRemovalAndNoChargeCompletionEnforceAuthenticationAndOwnership(string? actor, string? role, HttpStatusCode expected)
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 600000);
        await ApplyCheckoutVoucherAsync(buyer, order);
        using var client = Client(actor, role);
        using var remove = await client.DeleteAsync(VoucherRoute(order));
        using var complete = await client.PostAsync($"{Route}/{order.OrderNumber}/complete-no-charge", null);
        Assert.Equal(expected, remove.StatusCode);
        Assert.Equal(expected, complete.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Equal(VoucherRedemptionStatus.Reserved,
            (await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().VoucherRedemptions.SingleAsync()).RedemptionStatus);
    }

    [Fact]
    public async Task VoucherRemovalReleasesCapacityRestoresTotalAndAllowsReplacementWithoutExtraRow()
    {
        using var buyer = Client("buyer", "Customer");
        using var member = Client("member", "Member");
        var first = await CreateAsync(buyer, Purchase([new(_basic)]));
        var second = await CreateAsync(member, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(quota: 1);
        await SeedCheckoutVoucherAsync(code: "OTHER", discountType: DiscountType.FIXED, discountValue: 50000);
        await ApplyCheckoutVoucherAsync(buyer, first);
        var removed = await ReadAsync(await buyer.DeleteAsync(VoucherRoute(first)), HttpStatusCode.OK);
        var repeated = await ReadAsync(await buyer.DeleteAsync(VoucherRoute(first)), HttpStatusCode.OK);
        Assert.Equal(first.Subtotal, removed.AmountDue);
        Assert.Equal(0, removed.DiscountAmount);
        Assert.Null(removed.AppliedVoucher);
        Assert.Equal(removed.AmountDue, repeated.AmountDue);
        Assert.Contains("amount=500000", removed.VietQr!.ImageUrl);
        await ApplyCheckoutVoucherAsync(member, second);
        var replacement = await ApplyCheckoutVoucherAsync(buyer, first, "OTHER");
        Assert.Equal(450000, replacement.AmountDue);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var id = (await db.Orders.SingleAsync(row => row.OrderNumber == first.OrderNumber)).Id;
        Assert.Single(await db.VoucherRedemptions.Where(row => row.OrderId == id).ToListAsync());
    }

    [Theory]
    [InlineData(MembershipPaymentMethods.VietQr)]
    [InlineData(MembershipPaymentMethods.Cash)]
    public async Task VoucherRemovedOrderCanPayFullPriceWithoutConfirmingReleasedHistory(string method)
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)], method));
        var voucher = await SeedCheckoutVoucherAsync();
        var discounted = await ApplyCheckoutVoucherAsync(buyer, order);
        var removed = await ReadAsync(await buyer.DeleteAsync(VoucherRoute(order)), HttpStatusCode.OK);
        Assert.Equal(500000m, removed.AmountDue);
        Assert.Equal(0, removed.DiscountAmount);
        Assert.Null(removed.AppliedVoucher);
        if (method == MembershipPaymentMethods.VietQr)
        {
            using var obsoleteAmount = await NotifyAsync(discounted, id: 19105);
            Assert.Equal(HttpStatusCode.Conflict, obsoleteAmount.StatusCode);
            using var fullPrice = await NotifyAsync(removed, id: 19106);
            Assert.Equal(HttpStatusCode.OK, fullPrice.StatusCode);
        }
        else
        {
            using var staff = Client("staff", "Staff");
            var route = $"/api/staff/membership-purchases/{order.OrderNumber}/confirm-cash";
            using var obsoleteAmount = await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(discounted.AmountDue));
            Assert.Equal(HttpStatusCode.Conflict, obsoleteAmount.StatusCode);
            using var fullPrice = await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(removed.AmountDue));
            Assert.Equal(HttpStatusCode.OK, fullPrice.StatusCode);
        }
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(VoucherRedemptionStatus.Released, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        Assert.Equal(PaymentStatus.Paid, (await db.PaymentTransactions.SingleAsync()).PaymentStatus);
        Assert.Single(await db.MemberMemberships.ToListAsync());
        using var manager = Client("manager", "Manager");
        Assert.Equal(0, (await manager.GetFromJsonAsync<PromotionResponse>($"/api/manager/promotions/{voucher.Id}"))!.UsedCount);
    }

    [Theory]
    [InlineData(MembershipPaymentMethods.VietQr)]
    [InlineData(MembershipPaymentMethods.Cash)]
    public async Task VoucherTimeoutReleasesExactlyOnceAndMakesQuotaAvailable(string method)
    {
        using var buyer = Client("buyer", "Customer");
        using var member = Client("member", "Member");
        var original = await CreateAsync(buyer, Purchase([new(_basic)], method));
        var voucher = await SeedCheckoutVoucherAsync(quota: 1);
        var applied = await ApplyCheckoutVoucherAsync(buyer, original);
        _clock.Now = original.ExpiresAt;
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        var expired = await ReadAsync(await buyer.GetAsync($"{Route}/{original.OrderNumber}"), HttpStatusCode.OK);
        Assert.Equal("Expired", expired.OrderStatus);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(VoucherRedemptionStatus.Released, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
            Assert.Empty(await db.MemberMemberships.ToListAsync());
        }
        using var manager = Client("manager", "Manager");
        Assert.Equal(0, (await manager.GetFromJsonAsync<PromotionResponse>($"/api/manager/promotions/{voucher.Id}"))!.UsedCount);
        // The campaign spans the later cash deadline as well as the online deadline.
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Vouchers.SingleAsync()).EndAt = _clock.Now.UtcDateTime.AddDays(1);
            await db.SaveChangesAsync();
        }
        var next = await CreateAsync(member, Purchase([new(_basic)]));
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            // BaseEntity creation uses wall-clock UTC; align this new fixture order with the advanced test clock.
            (await db.Orders.SingleAsync(row => row.OrderNumber == next.OrderNumber)).CreatedAt = _clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        next = await ReadAsync(await member.GetAsync($"{Route}/{next.OrderNumber}"), HttpStatusCode.OK);
        await ApplyCheckoutVoucherAsync(member, next);
        if (method == MembershipPaymentMethods.VietQr)
        {
            using var late = await NotifyAsync(applied, id: 19111, at: original.CreatedAt.AddMinutes(3));
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        }
        else
        {
            using var staff = Client("staff", "Staff");
            using var late = await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{original.OrderNumber}/confirm-cash",
                new ConfirmMembershipCashRequest(applied.AmountDue));
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        }
    }

    [Fact]
    public async Task VoucherFailedPaymentAmountKeepsReservationUntilTimeout()
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, order);
        using var invalid = await NotifyAsync(applied, id: 19112, amount: 1);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(VoucherRedemptionStatus.Reserved, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        Assert.Equal(PaymentStatus.Pending, (await db.PaymentTransactions.SingleAsync()).PaymentStatus);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("payment")]
    [InlineData("timeout")]
    [InlineData("remove")]
    [InlineData("no-charge")]
    public async Task VoucherPersistenceFailureRollsBackFinancialRedemptionAndFulfillmentState(string operation)
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: operation == "no-charge" ? 600000 : 100000);
        var before = operation == "apply" ? order : await ApplyCheckoutVoucherAsync(buyer, order);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var trigger = operation == "apply" ? """
            CREATE TRIGGER reject_voucher_transition BEFORE INSERT ON VoucherRedemptions
            BEGIN
                SELECT RAISE(ABORT, 'test voucher persistence failure');
            END;
            """ : """
            CREATE TRIGGER reject_voucher_transition BEFORE UPDATE ON VoucherRedemptions
            BEGIN
                SELECT RAISE(ABORT, 'test voucher persistence failure');
            END;
            """;
        await db.Database.ExecuteSqlRawAsync(trigger);
        if (operation == "timeout")
        {
            _clock.Now = order.ExpiresAt;
            await Assert.ThrowsAnyAsync<Exception>(() => RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct)));
        }
        else
        {
            using var failed = operation switch
            {
                "apply" => await buyer.PostAsJsonAsync(VoucherRoute(order), new { voucherCode = "SAVE20" }),
                "payment" => await NotifyAsync(before, id: 19120),
                "remove" => await buyer.DeleteAsync(VoucherRoute(order)),
                _ => await buyer.PostAsync($"{Route}/{order.OrderNumber}/complete-no-charge", null)
            };
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
        Assert.Equal(OrderStatus.Pending, (await db.Orders.AsNoTracking().SingleAsync()).OrderStatus);
        var payment = await db.PaymentTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatus.Pending, payment.PaymentStatus);
        Assert.Equal(before.AmountDue, payment.AmountDue);
        Assert.Equal(operation == "apply" ? 0 : 1, await db.VoucherRedemptions.CountAsync());
        if (operation != "apply")
            Assert.Equal(VoucherRedemptionStatus.Reserved, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Empty(await db.MemberMemberships.ToListAsync());
        Assert.Equal(["Customer", "PT"], await RolesAsync(db, "buyer"));
    }

    [Fact]
    public async Task VoucherAbsentFlowStillUsesFullPriceAndCanSettleTimelyTransferAfterExpirationJob()
    {
        using var buyer = Client("buyer", "Customer");
        var order = await CreateAsync(buyer, Purchase([new(_basic)]));
        Assert.Equal(order.Subtotal, order.AmountDue);
        Assert.Equal(0, order.DiscountAmount);
        Assert.Null(order.AppliedVoucher);
        _clock.Now = order.ExpiresAt.AddMinutes(1);
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        using var transfer = await NotifyAsync(order, id: 19130, at: order.CreatedAt.AddMinutes(3));
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(500000m, (await db.PaymentTransactions.SingleAsync()).AmountReceived);
        Assert.Empty(await db.VoucherRedemptions.ToListAsync());
    }
}
