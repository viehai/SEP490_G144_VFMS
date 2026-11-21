using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// Fixes from the feature 4 review (2026-11-21).
public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task CorrectingPresentToAbsentKeepsAPendingFreezeSessionFree()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var first = contract.Sessions[0];
        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        // UC 4.9: the request carries the contract's freeze policy for the Manager.
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        Assert.Equal((7, 15), (freeze.MinimumDays, freeze.MaximumDays));

        _clock.Now = LocalTime(first.Date, first.EndTime).AddMinutes(5);
        using var present = await trainer.PostAsJsonAsync($"/api/pt/sessions/{first.Id}/attendance", true);
        Assert.True(present.StatusCode == HttpStatusCode.OK, await present.Content.ReadAsStringAsync());
        using var absent = await trainer.PostAsJsonAsync($"/api/pt/sessions/{first.Id}/attendance", false);
        Assert.True(absent.StatusCode == HttpStatusCode.OK, await absent.Content.ReadAsStringAsync());

        // UC 4.8 step 8.1 / GBR-97: the corrected absence returns to Pending Freeze instead of a forfeited No-Show.
        Assert.Equal("PendingFreeze", (await absent.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        Assert.Equal(0, (await ContractOf(member, id)).UsedCredits);
    }

    [Fact]
    public async Task ContractShowsItsReviewStateAndGymHours()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        Assert.False(contract.HasReview);
        Assert.Equal((new TimeOnly(7, 0), new TimeOnly(22, 0)), (contract.GymOpenTime, contract.GymCloseTime));

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.TrainerReviews.Add(new TrainerReview { PTPurchaseId = id, TrainerUserId = "buyer", MemberUserId = "member", Rating = 5 });
            await db.SaveChangesAsync();
        }
        // UC 4.10: a reviewed contract stays reviewed after a reload.
        Assert.True((await ContractOf(member, id)).HasReview);
    }

    [Fact]
    public async Task MakeUpPeriodKeepsTheContractAssignedAndThePTRole()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, _) = await ActivatedPTContract(member);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.PTPurchases.Include(p => p.Sessions).SingleAsync(p => p.Id == id);
        stored.EndDate = Today.AddDays(-5);
        var taught = stored.Sessions.OrderBy(s => s.SessionDate).First();
        taught.SessionStatus = PTSessionStatus.AutoCompleted;
        var profile = await db.TrainerProfiles.SingleAsync(p => p.PTUserId == "buyer");
        profile.AcceptingNewMembers = false;
        profile.NotAcceptingSince = Today.AddDays(-90);
        await db.SaveChangesAsync();

        // UC 4.6: still 'Currently Assigned' inside the 14-day make-up period; the last training counts auto-completed sessions.
        var current = (await trainer.GetFromJsonAsync<PagedResponse<PTAssignedMember>>(
            "/api/pt/assigned-members/page?historical=false"))!.Items;
        Assert.Equal(taught.SessionDate, Assert.Single(current, row => row.ContractId == id).LastTrainedOn);

        // GBR-98: the PT role is kept until the make-up period has also ended.
        var service = scope.ServiceProvider.GetRequiredService<PersonalTrainingService>();
        await service.ExpireInactiveTrainers(Today, default);
        Assert.True((await db.TrainerProfiles.AsNoTracking().SingleAsync(p => p.PTUserId == "buyer")).IsActive);
        stored.EndDate = Today.AddDays(-15);
        await db.SaveChangesAsync();
        await service.ExpireInactiveTrainers(Today, default);
        Assert.False((await db.TrainerProfiles.AsNoTracking().SingleAsync(p => p.PTUserId == "buyer")).IsActive);
    }

    [Fact]
    public async Task PTCanClearSlotsThatStillHaveBookedSessions()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);

        // UC 4.1 / GBR-08: the template change affects new purchases only; booked sessions keep their time.
        using var cleared = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest([]));
        Assert.True(cleared.StatusCode == HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        var after = await ContractOf(member, id);
        Assert.Equal(contract.Sessions.Select(s => (s.Date, s.StartTime, s.Status)),
            after.Sessions.Select(s => (s.Date, s.StartTime, s.Status)));
    }

    [Fact]
    public async Task PurchaseMovesABusyDateToTheEndOfTheSchedule()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        var plain = (await (await member.PostAsJsonAsync("/api/pt/schedule/preview", request))
            .Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Empty(plain.Skipped);
        var busy = plain.Sessions.First(s => s.Date >= request.StartDate.AddDays(7));
        await AddOtherMemberSession(busy);

        // GBR-12: a busy date after the start week (for example another Member's make-up) moves to the end.
        using var response = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var moved = (await response.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Equal(busy.Date, Assert.Single(moved.Skipped).Date);
        Assert.Equal(plain.Sessions.Count, moved.Sessions.Count);
        Assert.DoesNotContain(moved.Sessions, s => s.Date == busy.Date);
        Assert.True(moved.ExpiresOn > plain.ExpiresOn);

        // A slot busy in the start week is held by another schedule: it is blocked, not moved.
        await AddOtherMemberSession(plain.Sessions[0]);
        using var blocked = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var options = (await (await member.PostAsJsonAsync("/api/pt/schedule/booking-slots",
            new PTBookingSlotsRequest(request.TrainerId, request.PackageId, request.StartDate, request.TemplateIds)))
            .Content.ReadFromJsonAsync<PTWeeklySlot[]>())!;
        Assert.NotNull(options.Single(slot => slot.TemplateId == plain.Sessions[0].TemplateId).BlockedReason);
    }

    private async Task AddOtherMemberSession(PTCalendarSlot slot)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var membership = await db.MemberMemberships.SingleAsync();
        db.PTPurchases.Add(new PTPurchase
        {
            MemberUserId = "gift", PTUserId = "buyer", MemberMembershipId = membership.Id,
            StartDate = slot.Date, EndDate = slot.Date, Status = PTPurchaseStatus.Active,
            Sessions = [new PTSession { MemberUserId = "gift", PTUserId = "buyer", SessionDate = slot.Date,
                StartTime = slot.StartTime, EndTime = slot.EndTime }]
        });
        await db.SaveChangesAsync();
    }
}
