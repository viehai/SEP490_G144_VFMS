using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task LockerLegacyIssuedCashOrderCannotBeRequotedAndStillHandsOver()
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var staff = Client("staff", "Staff");
        var order = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals", new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var number = order.GetProperty("orderNumber").GetString();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.PaymentTransactions.SingleAsync(p => p.Order.OrderNumber == number)).PaymentStartedAt = null;
            await db.SaveChangesAsync();
        }
        var existing = await staff.GetFromJsonAsync<JsonElement>($"/api/lockers/staff/orders/{number}");
        Assert.False(existing.GetProperty("canChoosePayment").GetBoolean());
        using var change = await staff.PostAsJsonAsync($"/api/lockers/staff/orders/{number}/payment", new { paymentMethod = "VietQR" });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{order.GetProperty("rentalId").GetInt32()}/handover", new { termsAccepted = true, identityVerified = true, cashAmount = 350_000 });
        Assert.Equal(HttpStatusCode.OK, handover.StatusCode);
    }

    [Fact]
    public async Task LockerVoucherWalletChecksRentalFeeInsteadOfDepositInclusiveTotal()
    {
        var (lockerId, policyId) = await PrepareLocker();
        await SeedCheckoutVoucherAsync(minimumSpend: 300_000);
        using var member = Client("member", "Member");
        var order = await CreateLockerCheckout(member, "/api/lockers/rentals", new { lockerId, policyId });
        var vouchers = await member.GetFromJsonAsync<JsonElement>($"/api/my-vouchers?orderNumber={order.GetProperty("orderNumber").GetString()}");
        var voucher = Assert.Single(vouchers.EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, voucher.GetProperty("checkoutIssue").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockerVoucherKeepsOverdueFeesAndHandlesACompletelyDiscountedRenewal(bool overdue)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        var initial = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals", new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var id = initial.GetProperty("rentalId").GetInt32();
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/handover", new { termsAccepted = true, identityVerified = true, cashAmount = 350_000 });
        Assert.Equal(HttpStatusCode.OK, handover.StatusCode);
        var rental = await handover.Content.ReadFromJsonAsync<JsonElement>();
        var expiry = rental.GetProperty("expiresAt").GetDateTime();
        if (overdue) _clock.Now = new DateTimeOffset(expiry).AddDays(2).AddHours(10);
        await SeedCheckoutVoucherAsync(code: "FULLRENT", discountValue: 100, cap: 1_000_000);
        using var renewal = await member.PostAsJsonAsync($"/api/lockers/rentals/{id}/renew", new { policyId, chooseAtCheckout = true });
        Assert.Equal(HttpStatusCode.Created, renewal.StatusCode);
        var number = (await renewal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString();
        using var chosen = await member.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = "VietQR", voucherCode = "FULLRENT" });
        Assert.True(chosen.StatusCode == HttpStatusCode.OK, await chosen.Content.ReadAsStringAsync());
        var checkout = await chosen.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(250_000, checkout.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(overdue ? 30_000 : 0, checkout.GetProperty("amountDue").GetDecimal());
        Assert.Equal(overdue ? "Pending" : "Paid", checkout.GetProperty("status").GetString());
        if (!overdue)
        {
            Assert.Equal("NoCharge", checkout.GetProperty("paymentMethod").GetString());
            rental = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
            Assert.True(rental.GetProperty("expiresAt").GetDateTime() > expiry);
        }
        else
        {
            _clock.Now = _clock.Now.AddMinutes(21);
            using var sweep = _app.Services.CreateAsyncScope();
            await sweep.ServiceProvider.GetRequiredService<VFMS.Application.Interfaces.ILockerService>().Expire(default);
            var db = sweep.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(VoucherRedemptionStatus.Released, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        }
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("VietQR")]
    public async Task LockerOwnerChoosesPaymentAndVoucherDoesNotDiscountDeposit(string method)
    {
        var (lockerId, policyId) = await PrepareLocker();
        await SeedCheckoutVoucherAsync();
        using var member = Client("member", "Member");
        var order = await CreateLockerCheckout(member, "/api/lockers/rentals", new { lockerId, policyId });
        var number = order.GetProperty("orderNumber").GetString();
        using var selected = await member.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = method, voucherCode = "save20" });
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var checkout = await selected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(300_000, checkout.GetProperty("amountDue").GetDecimal());
        Assert.Equal(50_000, checkout.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(method, checkout.GetProperty("paymentMethod").GetString());
        Assert.False(checkout.GetProperty("canChoosePayment").GetBoolean());
        Assert.Equal(order.GetProperty("expiresAt").GetDateTime(), checkout.GetProperty("expiresAt").GetDateTime());
        Assert.Equal(method == "Cash", checkout.GetProperty("vietQr").ValueKind == JsonValueKind.Null);
        using var repeated = await member.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = method, voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        using var change = await member.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = method == "Cash" ? "VietQR" : "Cash" });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        using var cancel = await member.PostAsync($"/api/lockers/orders/{number}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(VoucherRedemptionStatus.Released, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockerStaffRenewalCanBePaidByOwnerWithVietQrAndVoucher(bool cancelByReturn)
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var staff = Client("staff", "Staff");
        using var member = Client("member", "Member");
        var initial = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals", new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        var id = initial.GetProperty("rentalId").GetInt32();
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/handover", new { termsAccepted = true, identityVerified = true, cashAmount = 350_000 });
        Assert.Equal(HttpStatusCode.OK, handover.StatusCode);
        var before = await handover.Content.ReadFromJsonAsync<JsonElement>();
        var expires = before.GetProperty("expiresAt").GetDateTime();
        await SeedCheckoutVoucherAsync();
        using var renewal = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/renew", new { policyId, paymentMethod = "Cash", chooseAtCheckout = true });
        Assert.Equal(HttpStatusCode.Created, renewal.StatusCode);
        var pending = await renewal.Content.ReadFromJsonAsync<JsonElement>();
        var number = pending.GetProperty("orderNumber").GetString();
        Assert.True(pending.GetProperty("canChoosePayment").GetBoolean());
        var current = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.Equal(number, current.GetProperty("pendingOrderNumber").GetString());
        Assert.Equal(expires, current.GetProperty("expiresAt").GetDateTime());
        using var chosen = await member.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = "VietQR", voucherCode = "SAVE20" });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var checkout = await chosen.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200_000, checkout.GetProperty("amountDue").GetDecimal());
        using var falseCash = await staff.PostAsJsonAsync($"/api/lockers/staff/orders/{number}/cash", new { amount = 200_000 });
        Assert.False(falseCash.IsSuccessStatusCode);
        if (cancelByReturn)
        {
            using var returned = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/return", new { lockerEmpty = true, keyLostOrDamaged = false });
            Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
            await using var cancelledScope = _app.Services.CreateAsyncScope();
            Assert.Equal(VoucherRedemptionStatus.Released, (await cancelledScope.ServiceProvider.GetRequiredService<VfmsDbContext>().VoucherRedemptions.SingleAsync()).RedemptionStatus);
            return;
        }
        var transfer = new DateTimeOffset(checkout.GetProperty("expiresAt").GetDateTime()).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(checkout.GetProperty("vietQr").GetProperty("transferContent").GetString()!, 200_000, 9851, transfer);
        Assert.True(paid.StatusCode == HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        current = await member.GetFromJsonAsync<JsonElement>("/api/lockers/my-rental");
        Assert.True(current.GetProperty("expiresAt").GetDateTime() > expires);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Equal(VoucherRedemptionStatus.Confirmed, (await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().VoucherRedemptions.SingleAsync()).RedemptionStatus);
    }

    [Fact]
    public async Task LockerCheckoutChoiceRejectsOtherMembersAndInvalidVouchersWithoutChangingAmount()
    {
        var (lockerId, policyId) = await PrepareLocker();
        using var member = Client("member", "Member");
        using var other = Client("buyer", "Member");
        var order = await CreateLockerCheckout(member, "/api/lockers/rentals", new { lockerId, policyId });
        var number = order.GetProperty("orderNumber").GetString();
        using var forbidden = await other.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = "Cash" });
        Assert.False(forbidden.IsSuccessStatusCode);
        using var invalid = await member.PostAsJsonAsync($"/api/lockers/orders/{number}/payment", new { paymentMethod = "VietQR", voucherCode = "UNKNOWN" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var current = await member.GetFromJsonAsync<JsonElement>($"/api/lockers/orders/{number}");
        Assert.Equal(350_000, current.GetProperty("amountDue").GetDecimal());
    }
}
