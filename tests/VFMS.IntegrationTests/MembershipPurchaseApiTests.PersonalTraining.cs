using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PTCheckoutReusesSepayAndOnlyAnOnTimeCallbackConfirmsHeldSessions(bool late)
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var previewResponse = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Equal(12, preview.Sessions.Count);

        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        Assert.Equal("Pending", checkout.Status);
        using var second = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var transferAt = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddMinutes(-14);
        _clock.Now = late ? new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddSeconds(1) : transferAt.AddSeconds(1);
        if (late)
            await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 91, transferAt);
        Assert.True(payment.StatusCode == HttpStatusCode.OK, await payment.Content.ReadAsStringAsync());
        using var replay = await NotifyTransferAsync(checkout.VietQr.TransferContent, checkout.AmountDue, 91, transferAt);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var contract = await db.PTPurchases.Include(p => p.Sessions).SingleAsync();
        Assert.Equal(late ? PTPurchaseStatus.Expired : PTPurchaseStatus.Active, contract.Status);
        Assert.Equal(12, contract.Sessions.Count);
        Assert.All(contract.Sessions, s => Assert.Equal(late ? PTSessionStatus.Cancelled : PTSessionStatus.Scheduled, s.SessionStatus));
        Assert.Equal(late ? 0 : 1, await db.Invoices.CountAsync());
        Assert.Equal(1, await db.MemberMemberships.CountAsync());
        if (late)
        {
            using var retry = await member.PostAsJsonAsync("/api/pt/purchases", request);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        }
    }

    [Fact]
    public async Task MemberApprovalMovesOneSessionWhileKeepingTheOriginalReservedUntilApproval()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var transferAt = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddMinutes(-14);
        _clock.Now = transferAt.AddSeconds(1);
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 92, transferAt);
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
        var path = $"/api/pt/contracts/{checkout.ContractId}";
        var contract = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        var session = contract.Sessions[0];
        var newTime = new TimeOnly(15, 0);
        using var proposed = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/proposal",
            new PTProposalRequest(session.Date, newTime, "Reschedule"));
        Assert.True(proposed.StatusCode == HttpStatusCode.OK, await proposed.Content.ReadAsStringAsync());
        var pending = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal(session.StartTime, pending.StartTime);
        Assert.Equal("ReschedulePending", pending.Status);
        var during = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        Assert.Equal(12, during.ReservedCredits);
        using var accepted = await member.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/proposal/decision",
            new PTProposalDecision(true, pending.Proposal!.ExpiresAt));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var moved = (await accepted.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal(newTime, moved.StartTime);
        Assert.Null(moved.Proposal);
    }

    [Fact]
    public async Task ThirteenCreditContractKeepsItsFreezePolicyAfterManagerEditsThePackage()
    {
        var request = await PreparePTCheckout(13, 5, 20);
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        using var trainer = Client("buyer", "PT");
        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var transferAt = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddMinutes(-14);
        _clock.Now = transferAt.AddSeconds(1);
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 93, transferAt);
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);

        var contractPath = $"/api/pt/contracts/{checkout.ContractId}";
        var before = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        Assert.Equal(13, before.Sessions.Count);
        Assert.Equal(20, before.RemainingFreezeDays);
        using var edited = await manager.PutAsJsonAsync($"/api/pt/manager/packages/{request.PackageId}",
            PackageInput(13, 5, 5));
        Assert.True(edited.StatusCode == HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var start = before.Sessions[0].Date;
        using var requested = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start, start.AddDays(7), "Travel", null));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        var protectedSessions = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single()
            .Sessions.Where(s => s.Status == "PendingFreeze").ToArray();
        Assert.Equal(3, protectedSessions.Length);
        var decision = $"/api/pt/manager/freezes/{freeze.Id}/decision";
        using var unauthorized = await trainer.PostAsJsonAsync(decision, new PTFreezeDecision(true, null));
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        using var approved = await manager.PostAsJsonAsync(decision, new PTFreezeDecision(true, null));
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var after = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        Assert.Equal(before.EndDate.AddDays(7), after.EndDate);
        Assert.Equal(13, after.ReservedCredits);
        Assert.Equal(13, after.RemainingFreezeDays);
        Assert.Equal(0, after.UsedCredits);
        Assert.Equal(3, after.Sessions.Count(s => s.Status == "Cancelled"));
        Assert.Equal(13, after.Sessions.Count(s => s.Status == "Scheduled"));

        using var nextCheckout = await member.PostAsJsonAsync("/api/pt/purchases",
            request with { StartDate = after.EndDate.AddDays(1) });
        Assert.True(nextCheckout.StatusCode == HttpStatusCode.Created, await nextCheckout.Content.ReadAsStringAsync());
        var next = (await nextCheckout.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var newContract = (await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{next.ContractId}"))!.Single();
        Assert.Equal(5, newContract.RemainingFreezeDays);
    }

    [Theory]
    [InlineData(0, 5, 20)]
    [InlineData(13, 0, 20)]
    [InlineData(13, 6, 5)]
    public async Task ManagerCannotCreatePackageWithInvalidCreditsOrFreezePolicy(int credits, int minimum, int maximum)
    {
        using var manager = Client("manager", "Manager");
        using var response = await manager.PostAsJsonAsync("/api/pt/manager/packages", PackageInput(credits, minimum, maximum));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static object PackageInput(int credits, int minimum, int maximum) => new
    {
        name = $"PT {credits}",
        credits,
        validityDays = 60,
        price = 600_000,
        trainerLevel = "Senior",
        freezeMinimumDays = minimum,
        freezeMaxTotalDays = maximum
    };

    private async Task<PTBookingRequest> PreparePTCheckout(int credits = 12, int minimum = 7, int maximum = 15)
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            gym.OpenTime = new(7, 0);
            gym.CloseTime = new(22, 0);
            var component = await db.PackageComponents.SingleAsync(c => c.PackageId == _basic);
            var order = new Order
            {
                PurchaserUserId = "member",
                OrderNumber = "PT-TEST-GYM",
                OrderStatus = OrderStatus.Paid,
                Items = [new OrderItem
                {
                    PackageId = _basic, BeneficiaryUserId = "member",
                    MemberMemberships = [new MemberMembership
                    {
                        MemberUserId = "member", PackageComponentId = component.Id,
                        MembershipStatus = MembershipStatus.Active, StartDate = Today, EndDate = Today.AddDays(200)
                    }]
                }]
            };
            db.Orders.Add(order);
            db.TrainerProfiles.Add(new TrainerProfile { PTUserId = "buyer", Level = TrainerLevel.Senior });
            await db.SaveChangesAsync();
        }

        using var manager = Client("manager", "Manager");
        using var create = await manager.PostAsJsonAsync("/api/pt/manager/packages",
            PackageInput(credits, minimum, maximum));
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var package = (await create.Content.ReadFromJsonAsync<PTPackageResponse>())!;
        using var trainer = Client("buyer", "PT");
        var slots = Enumerable.Range(1, 5).SelectMany(day => new[] { 9, 12, 18 }
            .Select(hour => new PTSlotInput((DayOfWeek)day, new TimeOnly(hour, 0)))).ToArray();
        using var published = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(slots));
        Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var start = Today.AddDays(2);
        var calendar = (await trainer.GetFromJsonAsync<PTCalendarSlot[]>($"/api/pt/trainers/buyer/calendar?from={start:yyyy-MM-dd}"))!;
        var pattern = calendar.Where(s => s.StartTime.Hour == 18 &&
            s.Date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
            .Select(s => s.TemplateId).Distinct().ToArray();
        Assert.Equal(3, pattern.Length);
        return new("buyer", package.Id, start, pattern, null);
    }
}
