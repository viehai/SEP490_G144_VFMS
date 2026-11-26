using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// NewScope (UC 4.4): a Member may pay a PT order in cash at the front desk; Staff confirms it.
public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task PTCounterPaymentHoldsSlotsForADayAndOnlyPTCashConfirmationActivatesIt()
    {
        var booking = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        var hold = await HoldPT(member, booking);
        var unselected = Assert.Single((await StaffOrdersAsync(staff, "?serviceType=PT&method=Unselected&paymentStatus=Unrecorded")).Items);
        Assert.Equal(hold.OrderNumber, unselected.OrderNumber);
        Assert.False(unselected.CanConfirmCash);
        using var choose = await member.PostAsJsonAsync($"/api/pt/purchases/{hold.OrderNumber}/payment",
            new PTPaymentRequest("PT-OFF", MembershipPaymentMethods.Cash));
        Assert.True(choose.StatusCode == HttpStatusCode.OK, await choose.Content.ReadAsStringAsync());
        var counter = (await choose.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        Assert.True(counter.PayAtCounter);
        Assert.Null(counter.VietQr);
        Assert.Equal("Pending", counter.Status);
        Assert.Equal(550_000m, counter.AmountDue);
        Assert.Equal(hold.ExpiresAt.AddHours(24).AddMinutes(-15), counter.HoldExpiresAt);
        Assert.Equal(counter.HoldExpiresAt, counter.ExpiresAt);

        // Past the 15-minute QR deadline the counter order is still waiting for cash, not a transfer.
        _clock.Now = new DateTimeOffset(hold.ExpiresAt).AddMinutes(30);
        Assert.Equal("Pending", (await member.GetFromJsonAsync<PTCheckoutResponse>($"/api/pt/purchases/{hold.OrderNumber}"))!.Status);
        var listed = Assert.Single((await StaffOrdersAsync(staff, "?serviceType=PT&method=Cash&status=Pending&paymentStatus=Pending")).Items);
        Assert.Equal(hold.OrderNumber, listed.OrderNumber);
        Assert.True(listed.IsPersonalTraining);
        Assert.True(listed.CanConfirmCash);
        Assert.Equal(550_000m, listed.AmountDue);
        Assert.Equal(new DateTimeOffset(counter.HoldExpiresAt), listed.PaymentDeadline);

        var route = $"/api/pt/purchases/{hold.OrderNumber}/cash-confirmation";
        using var membershipCash = await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{hold.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(550_000m));
        Assert.Equal(HttpStatusCode.NotFound, membershipCash.StatusCode);
        using var byMember = await member.PostAsJsonAsync(route, new PTCashConfirmationRequest(550_000m));
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        using var otherGym = Client("other-staff", "Staff");
        using var outsider = await otherGym.PostAsJsonAsync(route, new PTCashConfirmationRequest(550_000m));
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        using var wrongAmount = await staff.PostAsJsonAsync(route, new PTCashConfirmationRequest(600_000m));
        Assert.Equal(HttpStatusCode.Conflict, wrongAmount.StatusCode);

        using var confirmed = await staff.PostAsJsonAsync(route, new PTCashConfirmationRequest(550_000m));
        Assert.True(confirmed.StatusCode == HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        var paid = (await confirmed.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        Assert.Equal("Paid", paid.Status);
        Assert.NotNull(paid.InvoiceNumber);
        var listedPaid = Assert.Single((await StaffOrdersAsync(staff, "?serviceType=PT&method=Cash&status=Paid")).Items);
        Assert.False(listedPaid.CanConfirmCash);
        Assert.Equal(paid.InvoiceNumber, listedPaid.InvoiceNumber);
        using var retry = await staff.PostAsJsonAsync(route, new PTCashConfirmationRequest(550_000m));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        var contract = Assert.Single((await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{hold.ContractId}"))!);
        Assert.Equal("Active", contract.Status);
        Assert.All(contract.Sessions, session => Assert.Equal("Scheduled", session.Status));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var payment = await db.PaymentTransactions.SingleAsync();
        Assert.Equal((PaymentMethod.Cash, PaymentStatus.Paid, 550_000m), (payment.PaymentMethod, payment.PaymentStatus, payment.AmountReceived));
        Assert.Equal(VoucherRedemptionStatus.Confirmed, (await db.VoucherRedemptions.SingleAsync()).RedemptionStatus);
        Assert.Contains(await db.AuditLogs.ToListAsync(), log => log.ActorUserId == "staff" && log.NewValue == "CashPaymentConfirmed");
    }

    [Fact]
    public async Task PTCounterOrderExpiresAfterADayAndPaymentChoiceClosesAfterTenMinutes()
    {
        var booking = await PreparePTCheckout();
        using var member = Client("member", "Member");
        var hold = await HoldPT(member, booking);
        using var choose = await member.PostAsJsonAsync($"/api/pt/purchases/{hold.OrderNumber}/payment",
            new PTPaymentRequest(null, MembershipPaymentMethods.Cash));
        var counter = (await choose.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;

        _clock.Now = new DateTimeOffset(counter.HoldExpiresAt).AddSeconds(1);
        await using (var scope = _app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<VFMS.Application.Interfaces.IPersonalTrainingService>().ExpirePending(CancellationToken.None);
        using var staff = Client("staff", "Staff");
        using var late = await staff.PostAsJsonAsync($"/api/pt/purchases/{hold.OrderNumber}/cash-confirmation",
            new PTCashConfirmationRequest(counter.AmountDue));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("Expired", (await member.GetFromJsonAsync<PTCheckoutResponse>($"/api/pt/purchases/{hold.OrderNumber}"))!.Status);
        // UC 4.5: an expired unpaid checkout is not listed under "My PT Packages".
        Assert.Empty((await member.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PTContractResponse>>(
            "/api/pt/contracts/page?page=1"))!.Items);

        // Use another future schedule after the first checkout has expired.
        var second = await HoldPT(member, booking with { StartDate = booking.StartDate.AddDays(7) });
        Assert.NotNull(second.ProceedBefore);
        _clock.Now = new DateTimeOffset(second.ProceedBefore!.Value).AddSeconds(1);
        using var tooLate = await member.PostAsJsonAsync($"/api/pt/purchases/{second.OrderNumber}/payment",
            new PTPaymentRequest(null, MembershipPaymentMethods.Cash));
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Contains("PT_PAYMENT_WINDOW_CLOSED", await tooLate.Content.ReadAsStringAsync());
    }

    private static async Task<PTCheckoutResponse> HoldPT(HttpClient member, PTBookingRequest booking)
    {
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", booking);
        Assert.True(hold.StatusCode == HttpStatusCode.Created, await hold.Content.ReadAsStringAsync());
        return (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
    }
}
