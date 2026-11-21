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
        var ordered = stored.Sessions.OrderBy(s => s.SessionDate).ToArray();
        var taught = ordered[0];
        taught.SessionStatus = PTSessionStatus.AutoCompleted;
        var unit = ordered[1];
        unit.SessionStatus = PTSessionStatus.Cancelled;
        unit.CancelledAt = _clock.Now.UtcDateTime;
        var profile = await db.TrainerProfiles.SingleAsync(p => p.PTUserId == "buyer");
        profile.AcceptingNewMembers = false;
        profile.NotAcceptingSince = Today.AddDays(-90);
        await db.SaveChangesAsync();
        const string currentPath = "/api/pt/assigned-members/page?historical=false";
        const string endedPath = "/api/pt/assigned-members/page?historical=true";

        // UC 4.6: with a make-up unit left, still 'Currently Assigned' inside the 14-day make-up period;
        // the last training counts auto-completed sessions.
        var current = (await trainer.GetFromJsonAsync<PagedResponse<PTAssignedMember>>(currentPath))!.Items;
        Assert.Equal(taught.SessionDate, Assert.Single(current, row => row.ContractId == id).LastTrainedOn);

        // GBR-98: the PT role is kept while the contract still has make-up to use.
        var service = scope.ServiceProvider.GetRequiredService<PersonalTrainingService>();
        await service.ExpireInactiveTrainers(Today, default);
        Assert.True((await db.TrainerProfiles.AsNoTracking().SingleAsync(p => p.PTUserId == "buyer")).IsActive);

        // Without make-up left, the ended contract moves to 'Ended'; the 60 days count from that day, then the PT role goes.
        unit.IsCreditForfeited = true;
        await db.SaveChangesAsync();
        Assert.DoesNotContain((await trainer.GetFromJsonAsync<PagedResponse<PTAssignedMember>>(currentPath))!.Items,
            row => row.ContractId == id);
        Assert.Contains((await trainer.GetFromJsonAsync<PagedResponse<PTAssignedMember>>(endedPath))!.Items,
            row => row.ContractId == id);
        await service.ExpireInactiveTrainers(Today, default);
        Assert.True((await db.TrainerProfiles.AsNoTracking().SingleAsync(p => p.PTUserId == "buyer")).IsActive);
        await service.ExpireInactiveTrainers(Today.AddDays(61), default);
        Assert.False((await db.TrainerProfiles.AsNoTracking().SingleAsync(p => p.PTUserId == "buyer")).IsActive);
    }

    [Fact]
    public async Task PTCanProposeAnyFreeWholeHourAndConflictsNameTheBusyParty()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var session = contract.Sessions[0];
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var published = await db.PTAvailabilities.Where(a => a.PTUserId == "buyer" && a.DayOfWeek == session.Date.DayOfWeek)
            .Select(a => a.StartTime).ToListAsync();
        var optionsPath = $"/api/pt/sessions/{session.Id}/reschedule-options?from={session.Date:yyyy-MM-dd}";

        // UC 4.7 step 3: the PT sees every whole hour of the day within gym hours, not only published slots.
        var options = (await trainer.GetFromJsonAsync<PTRescheduleSlot[]>(optionsPath))!;
        var free = options.Where(s => s.Status == "Available" && !published.Contains(s.StartTime))
            .Select(s => s.StartTime).ToArray();
        Assert.True(free.Length >= 2);

        // The Member trains with another PT at the first free hour: MSG-32, and the PT sees it as Busy.
        var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
        var roles = await db.ApplicationRoles.ToDictionaryAsync(role => role.RoleName);
        db.ApplicationUsers.Add(User("other-pt", gym, roles["PT"]));
        db.PTPurchases.Add(new PTPurchase
        {
            MemberUserId = "member",
            PTUserId = "other-pt",
            MemberMembershipId = (await db.MemberMemberships.FirstAsync()).Id,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Sessions = [new PTSession { MemberUserId = "member", PTUserId = "other-pt", SessionDate = session.Date,
                StartTime = free[0], EndTime = free[0].AddHours(1) }]
        });
        await db.SaveChangesAsync();
        var proposalPath = $"/api/pt/sessions/{session.Id}/proposal";
        using var memberBusy = await trainer.PostAsJsonAsync(proposalPath, new PTProposalRequest(session.Date, free[0], "Reschedule"));
        Assert.Equal(HttpStatusCode.Conflict, memberBusy.StatusCode);
        Assert.Contains("PT_MEMBER_SCHEDULE_CONFLICT", await memberBusy.Content.ReadAsStringAsync());
        Assert.Contains((await trainer.GetFromJsonAsync<PTRescheduleSlot[]>(optionsPath))!,
            slot => slot.StartTime == free[0] && slot.Status == "Busy");

        // An unpublished free hour: the Member accepts and the session moves without using a credit.
        using var proposed = await trainer.PostAsJsonAsync(proposalPath, new PTProposalRequest(session.Date, free[1], "Reschedule"));
        Assert.True(proposed.StatusCode == HttpStatusCode.OK, await proposed.Content.ReadAsStringAsync());
        var pending = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        using var accepted = await member.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/proposal/decision",
            new PTProposalDecision(true, pending.Proposal!.ExpiresAt));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var after = await ContractOf(member, id);
        var moved = Assert.Single(after.Sessions, s => s.Id == session.Id);
        Assert.Equal((free[1], "Scheduled"), (moved.StartTime, moved.Status));
        Assert.Equal(0, after.UsedCredits);
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
