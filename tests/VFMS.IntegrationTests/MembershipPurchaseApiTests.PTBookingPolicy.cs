using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task PTBookingEnforces24HoursBeforeFirstSessionOnAnyWeekday(int secondsPastBoundary, bool allowed)
    {
        _clock.Now = new DateTimeOffset(2026, 11, 23, 11, 0, 0, TimeSpan.Zero); // Monday 18:00 in Vietnam.
        var request = await PreparePTCheckout(3);
        using var member = Client("member", "Member");
        var template = (await member.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/buyer/weekly-template"))!;
        var tuesday = template.Single(s => s.DayOfWeek == DayOfWeek.Tuesday && s.StartTime.Hour == 18);
        request = request with { StartDate = new(2026, 11, 24), TemplateIds = [tuesday.TemplateId] };
        _clock.Now = _clock.Now.AddSeconds(secondsPastBoundary);

        using var slots = await member.PostAsJsonAsync("/api/pt/schedule/booking-slots",
            new PTBookingSlotsRequest(request.TrainerId, request.PackageId, request.StartDate, []));
        Assert.Equal(HttpStatusCode.OK, slots.StatusCode);
        var choices = (await slots.Content.ReadFromJsonAsync<PTWeeklySlot[]>())!;
        Assert.Equal(allowed, choices.Single(s => s.TemplateId == tuesday.TemplateId).BlockedReason is null);
        using var preview = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(allowed, preview.IsSuccessStatusCode);
        if (allowed)
        {
            var schedule = (await preview.Content.ReadFromJsonAsync<PTBookingPreview>())!;
            Assert.Equal(new DateOnly(2026, 11, 24), schedule.Sessions[0].Date);
            Assert.All(schedule.Sessions, s => Assert.Equal(DayOfWeek.Tuesday, s.Date.DayOfWeek));
        }
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.BadRequest, hold.StatusCode);
    }

    [Theory]
    [InlineData("member", "Member", false)]
    [InlineData("member", "Member", true)]
    [InlineData("gift", "Customer", false)]
    [InlineData("gift", "Customer", true)]
    public async Task PTCanBePurchasedWithoutGymMembership(string userId, string role, bool cash)
    {
        var request = await PreparePTCheckout(3);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.MemberMemberships.RemoveRange(await db.MemberMemberships.ToListAsync());
            await db.SaveChangesAsync();
        }
        using var customer = Client(userId, role);
        using var hold = await customer.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(hold.StatusCode == HttpStatusCode.Created, await hold.Content.ReadAsStringAsync());
        var checkout = (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        using var payment = await customer.PostAsJsonAsync($"/api/pt/purchases/{checkout.OrderNumber}/payment",
            new PTPaymentRequest(PaymentMethod: cash ? "Cash" : "VietQR"));
        Assert.True(payment.StatusCode == HttpStatusCode.OK, await payment.Content.ReadAsStringAsync());
        checkout = (await payment.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        if (cash)
        {
            using var staff = Client("staff", "Staff");
            using var confirmed = await staff.PostAsJsonAsync($"/api/pt/purchases/{checkout.OrderNumber}/cash-confirmation",
                new PTCashConfirmationRequest(checkout.AmountDue));
            Assert.True(confirmed.StatusCode == HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        }
        else
        {
            var transfer = new DateTimeOffset(checkout.ExpiresAt).AddMinutes(-14);
            _clock.Now = transfer.AddSeconds(1);
            using var paid = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 19001, transfer);
            Assert.True(paid.StatusCode == HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        }
        using var status = await customer.GetAsync($"/api/pt/purchases/{checkout.OrderNumber}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var contracts = (await customer.GetFromJsonAsync<PTContractResponse[]>("/api/pt/contracts"))!;
        Assert.Equal("Active", Assert.Single(contracts).Status);
        using var details = await customer.GetAsync($"/api/pt/contracts/{checkout.ContractId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var freezes = await customer.GetAsync($"/api/pt/contracts/{checkout.ContractId}/freezes?page=1");
        Assert.Equal(HttpStatusCode.OK, freezes.StatusCode);
        await using var verifiedScope = _app.Services.CreateAsyncScope();
        var verified = verifiedScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var purchase = await verified.PTPurchases.SingleAsync();
        Assert.Null(purchase.MemberMembershipId);
        Assert.Equal(PTPurchaseStatus.Active, purchase.Status);
    }
}
