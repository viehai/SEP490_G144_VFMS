using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Services;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// UC 4.8 / 4.9 spec audit fixes (2026-11-21).
public sealed partial class MembershipPurchaseApiTests
{
    private static PTCalendarSlot SlotOf(PTPlannedSession s) => new(0, s.Date, s.StartTime, s.EndTime, "Busy");

    [Fact]
    public async Task FreezeRequestRejectsAPastStartAndADurationOutsideThePolicy()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        var path = $"/api/pt/contracts/{id}/freezes";
        var today = DateOnly.FromDateTime(_clock.Now.UtcDateTime.AddHours(7));

        using var past = await member.PostAsJsonAsync(path, new PTFreezeRequest(today.AddDays(-1), today.AddDays(7), "Travel"));
        Assert.Equal(HttpStatusCode.Conflict, past.StatusCode);
        using var afterEnd = await member.PostAsJsonAsync(path,
            new PTFreezeRequest(contract.EndDate.AddDays(1), contract.EndDate.AddDays(9), "Travel"));
        Assert.Equal(HttpStatusCode.Conflict, afterEnd.StatusCode);
        foreach (var days in new[] { 6, 16 }) // policy 7-15 days
        {
            using var outside = await member.PostAsJsonAsync(path,
                new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(days), "Travel"));
            Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
            Assert.Contains("PT_FREEZE_DURATION", await outside.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task ABusyPatternSlotAfterTheFreezeIsLeftForTheMemberToChoose()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        var freeze = new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel");
        var path = $"/api/pt/contracts/{id}/freezes";
        var free = (await (await member.PostAsJsonAsync($"{path}/preview", freeze)).Content.ReadFromJsonAsync<PTFreezeProposal>())!;
        Assert.Equal(3, free.Sessions.Count);
        await AddOtherMemberSession(SlotOf(free.Sessions[0]));

        var busy = (await (await member.PostAsJsonAsync($"{path}/preview", freeze)).Content.ReadFromJsonAsync<PTFreezeProposal>())!;
        // UC 4.8 step 5: the busy session is not moved on; the other sessions keep the pattern.
        Assert.Equal(free.Sessions[0].OriginalSessionId, Assert.Single(busy.SessionsNeedingSlot));
        Assert.Equal(free.Sessions.Skip(1).Select(s => (s.Date, s.StartTime)), busy.Sessions.Select(s => (s.Date, s.StartTime)));
        using var blocked = await member.PostAsJsonAsync(path, freeze);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("PT_FREEZE_SLOT_REQUIRED", await blocked.Content.ReadAsStringAsync());

        var last = free.Sessions[^1];
        var chosen = new PTEarlyResumeMove(free.Sessions[0].OriginalSessionId, last.Date.AddDays(7), last.StartTime);
        using var requested = await member.PostAsJsonAsync(path, freeze with { Moves = [chosen] });
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MemberChangesAnUnavailableHeldSlotAfterTheStartAndTheManagerThenApproves()
    {
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        var slotsPath = $"/api/pt/freezes/{freeze.Id}/slots";
        var held = (await member.GetFromJsonAsync<PTFreezeProposal>(slotsPath))!;
        Assert.Empty(held.SessionsNeedingSlot);
        var lost = held.Sessions[0];
        await AddOtherMemberSession(SlotOf(lost));
        var manage = (await manager.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PTFreezeResponse>>(
            "/api/pt/manager/freezes/page?processed=false&page=1"))!.Items.Single();
        Assert.Equal(3, manage.ProposedSlots.Count);

        // The freeze has started: the request can no longer be withdrawn, but its slots can still change (UC 4.8 step 8.4).
        _clock.Now = LocalTime(contract.StartDate, new TimeOnly(23, 0));
        using var withdraw = await member.PostAsync($"/api/pt/freezes/{freeze.Id}/withdraw", null);
        Assert.Equal(HttpStatusCode.Conflict, withdraw.StatusCode);
        using var blocked = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision", new PTFreezeDecision(true, null));
        Assert.Contains("PT_FREEZE_SLOT_UNAVAILABLE", await blocked.Content.ReadAsStringAsync());
        held = (await member.GetFromJsonAsync<PTFreezeProposal>(slotsPath))!;
        Assert.Equal(lost.OriginalSessionId, Assert.Single(held.SessionsNeedingSlot));

        using var taken = await member.PutAsJsonAsync(slotsPath, new PTFreezeSlotChange(
            [new(lost.OriginalSessionId, held.Sessions[1].Date, held.Sessions[1].StartTime)]));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        var next = new PTEarlyResumeMove(lost.OriginalSessionId, held.Sessions[^1].Date.AddDays(7), held.Sessions[^1].StartTime);
        using var changed = await member.PutAsJsonAsync(slotsPath, new PTFreezeSlotChange([next]));
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        Assert.Equal("Pending", (await changed.Content.ReadFromJsonAsync<PTFreezeResponse>())!.Status);
        var protectedNow = await ContractOf(member, id);
        Assert.Equal(3, protectedNow.Sessions.Count(s => s.Status == "PendingFreeze"));

        using var approved = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision", new PTFreezeDecision(true, null));
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var after = await ContractOf(member, id);
        Assert.Contains(after.Sessions, s => s.Status == "Scheduled" && s.Date == next.Date && s.StartTime == next.StartTime);
        Assert.Equal(next.Date, after.EndDate);
    }

    [Fact]
    public async Task RejectingAfterTheStartTurnsPassedProtectedSessionsIntoMakeUps()
    {
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        var first = contract.Sessions[0];
        _clock.Now = LocalTime(first.Date, first.EndTime).AddHours(1);

        using var noReason = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision", new PTFreezeDecision(false, " "));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        using var rejected = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision", new PTFreezeDecision(false, "No proof"));
        Assert.True(rejected.StatusCode == HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var after = await ContractOf(member, id);
        Assert.Equal("Cancelled", after.Sessions.Single(s => s.Id == first.Id).Status);
        Assert.All(contract.Sessions.Skip(1).Where(s => s.Date < contract.StartDate.AddDays(7)),
            s => Assert.Equal("Scheduled", after.Sessions.Single(x => x.Id == s.Id).Status));
        Assert.DoesNotContain(after.Sessions, s => s.Status is "PendingFreeze" or "FreezeHold");
        Assert.False(after.FreezeUsed);
    }

    [Fact]
    public async Task ManagersAreRemindedAt24HoursAndDailyAfter48Hours()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        var created = _clock.Now;

        async Task<int> Reminders(double hours)
        {
            _clock.Now = created.AddHours(hours);
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<VFMS.Application.Interfaces.IPersonalTrainingService>().RemindPendingFreezes(default);
            return await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Notifications
                .CountAsync(n => n.Title == "Nhắc duyệt bảo lưu PT");
        }

        Assert.Equal(0, await Reminders(23));
        var first = await Reminders(25);
        Assert.True(first > 0);
        Assert.Equal(first, await Reminders(40));
        Assert.Equal(2 * first, await Reminders(49.5));
        Assert.Equal(2 * first, await Reminders(60));
        Assert.Equal(3 * first, await Reminders(74));
    }

    [Fact]
    public async Task FreezePlanningDoesNotDependOnGymMembership()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        var freeze = new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel");
        var path = $"/api/pt/contracts/{id}/freezes/preview";
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.MemberMemberships.RemoveRange(await db.MemberMemberships.ToListAsync());
            await db.SaveChangesAsync();
        }
        using var preview = await member.PostAsJsonAsync(path, freeze);
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var plan = (await preview.Content.ReadFromJsonAsync<PTFreezeProposal>())!;
        Assert.NotEmpty(plan.Sessions);
        Assert.Empty(plan.SessionsNeedingSlot);
        Assert.True(plan.ExpiresOn > contract.EndDate);
    }

    [Fact]
    public async Task FreezeWithAReplacementTrainerIsRejectedAndKeepsTheAssignedTrainer()
    {
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            var ptRole = await db.ApplicationRoles.SingleAsync(r => r.RoleName == "PT");
            db.ApplicationUsers.Add(User("pt2", gym, ptRole));
            db.TrainerProfiles.Add(new TrainerProfile
            {
                PTUserId = "pt2",
                Level = TrainerLevel.Senior,
                AcceptingNewMembers = true,
                TrainerRoleId = _ptTrainerRole
            });
            await db.SaveChangesAsync();
        }
        using var newTrainer = Client("pt2", "PT");
        using var published = await newTrainer.PutAsJsonAsync("/api/pt/availability",
            new PTPublishRequest(Enumerable.Range(1, 5).Select(day => new PTSlotInput((DayOfWeek)day, new TimeOnly(9, 0))).ToArray()));
        Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var template = (await newTrainer.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/pt2/weekly-template"))!;
        var ids = template.Where(s => s.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday).Select(s => s.TemplateId).ToArray();

        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel", null, "pt2", ids));
        Assert.Equal(HttpStatusCode.Conflict, requested.StatusCode);
        var after = await ContractOf(member, id);
        Assert.Equal("buyer", after.TrainerId);
        Assert.Equal(contract.TotalCredits, after.ReservedCredits);
        Assert.Equal(contract.Sessions, after.Sessions);
        Assert.False(after.HasPendingOrApprovedFreeze);
    }
}
