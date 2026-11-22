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

// Group 4 flows added for Report 3 (UC 4.6-4.13).
public sealed partial class MembershipPurchaseApiTests
{
    private async Task<(int ContractId, PTContractResponse Contract)> ActivatedPTContract(HttpClient member)
    {
        var request = await PreparePTCheckout();
        using var held = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, held.StatusCode);
        var order = await IssueQr(member, (await held.Content.ReadFromJsonAsync<PTCheckoutResponse>())!);
        var transfer = new DateTimeOffset(order.ExpiresAt).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(order.VietQr!.TransferContent, order.AmountDue, 140, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        return (order.ContractId, (await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{order.ContractId}"))!.Single());
    }

    private static DateTimeOffset LocalTime(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), TimeSpan.FromHours(7));

    private async Task<PTContractResponse> ContractOf(HttpClient member, int id) =>
        (await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{id}"))!.Single();

    [Fact]
    public async Task PTAbsentReportsBecomeMakeUpsUnlessThePTRecordedPresentWhichTheManagerDecides()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        var first = contract.Sessions[0];
        var second = contract.Sessions[1];

        // Not recorded by the PT: the Member's report turns the session into a make-up unit.
        _clock.Now = LocalTime(first.Date, first.EndTime).AddMinutes(5);
        using var reported = await member.PostAsJsonAsync($"/api/pt/sessions/{first.Id}/trainer-absence",
            new PTTrainerAbsenceReportRequest());
        Assert.True(reported.StatusCode == HttpStatusCode.OK, await reported.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", (await reported.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        // GBR-103: the make-up unit is usable only after the PT's response period (end of the next day).
        var waiting = await ContractOf(member, id);
        Assert.Equal(0, waiting.MakeUpUnits);
        Assert.Equal(waiting.EndDate.AddDays(14), waiting.MakeUpDeadline);   // UC 4.5: shown while the unit waits too
        Assert.Equal(LocalTime(first.Date.AddDays(2), TimeOnly.MinValue).UtcDateTime,
            waiting.Sessions.Single(s => s.Id == first.Id).MakeUpAvailableAt);
        _clock.Now = LocalTime(first.Date.AddDays(2), new TimeOnly(0, 1));
        var afterFirst = await ContractOf(member, id);
        Assert.Equal(1, afterFirst.MakeUpUnits);
        Assert.Equal(afterFirst.EndDate.AddDays(14), afterFirst.MakeUpDeadline);

        // PT recorded Present: the report becomes a dispute and the Manager decides.
        _clock.Now = LocalTime(second.Date, second.EndTime).AddMinutes(5);
        using var present = await trainer.PostAsJsonAsync($"/api/pt/sessions/{second.Id}/attendance", true);
        Assert.True(present.StatusCode == HttpStatusCode.OK, await present.Content.ReadAsStringAsync());
        using var disputed = await member.PostAsJsonAsync($"/api/pt/sessions/{second.Id}/trainer-absence",
            new PTTrainerAbsenceReportRequest("PT did not come"));
        Assert.Equal("Disputed", (await disputed.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        var pending = (await manager.GetFromJsonAsync<PagedResponse<PTAttendanceDispute>>(
            "/api/pt/manager/attendance-disputes?processed=false&page=1"))!.Items;
        Assert.Equal(second.Id, Assert.Single(pending).SessionId);
        using var invalid = await manager.PostAsJsonAsync($"/api/pt/manager/attendance-disputes/{second.Id}/decision",
            new PTAttendanceDisputeDecision("Approve", "Checked camera"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var decided = await manager.PostAsJsonAsync($"/api/pt/manager/attendance-disputes/{second.Id}/decision",
            new PTAttendanceDisputeDecision(PTDisputeDecisions.PTAbsent, "Front desk confirmed the PT was away"));
        Assert.True(decided.StatusCode == HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());
        _clock.Now = LocalTime(second.Date.AddDays(2), new TimeOnly(0, 1));   // both units past the PT's response period
        Assert.Equal(2, (await ContractOf(member, id)).MakeUpUnits);
        var processed = (await manager.GetFromJsonAsync<PagedResponse<PTAttendanceDispute>>(
            "/api/pt/manager/attendance-disputes?processed=true&page=1"))!.Items;
        Assert.Equal(PTDisputeDecisions.PTAbsent, Assert.Single(processed).Decision);
    }

    [Fact]
    public async Task AttendanceIsSameDayOnlyAndUnrecordedSessionsAutoCompleteNextDay()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var session = contract.Sessions[0];

        _clock.Now = LocalTime(session.Date, session.StartTime).AddMinutes(10);
        using var early = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/attendance", true);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        _clock.Now = LocalTime(session.Date.AddDays(1), new TimeOnly(1, 0));
        await using (var scope = _app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PersonalTrainingService>().ProcessAttendance(CancellationToken.None);
        using var late = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/attendance", false);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        var after = await ContractOf(member, id);
        Assert.Equal("AutoCompleted", after.Sessions.Single(s => s.Id == session.Id).Status);
        Assert.Equal(1, after.UsedCredits);

        // UC 4.6 step 9: the coaching note keeps its time and its author.
        using var noted = await trainer.PutAsJsonAsync($"/api/pt/sessions/{session.Id}/note", new PTNoteRequest("Giữ nhịp thở tốt."));
        Assert.True(noted.StatusCode == HttpStatusCode.OK, await noted.Content.ReadAsStringAsync());
        await using var check = _app.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<VfmsDbContext>().PTSessions.AsNoTracking()
            .SingleAsync(s => s.Id == session.Id);
        Assert.Equal(("buyer", "Giữ nhịp thở tốt."), (stored.NoteAuthorUserId, stored.Note));
        Assert.NotNull(stored.NoteUpdatedAt);
    }

    [Fact]
    public async Task EitherPartyCanAskToCancelAtLeast12HoursAheadAndAcceptanceReturnsAMakeUp()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var first = contract.Sessions[0];
        var second = contract.Sessions[1];

        _clock.Now = LocalTime(first.Date, first.StartTime).AddHours(-11);
        using var tooLate = await member.PostAsync($"/api/pt/sessions/{first.Id}/cancellation-request", null);
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);

        _clock.Now = LocalTime(first.Date, first.StartTime).AddHours(-24);
        using var requested = await member.PostAsync($"/api/pt/sessions/{first.Id}/cancellation-request", null);
        Assert.True(requested.StatusCode == HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        var pending = (await requested.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal("Cancellation", pending.Proposal!.Type);
        Assert.Equal(LocalTime(first.Date, first.StartTime).AddHours(-4).UtcDateTime, pending.Proposal.ExpiresAt);
        using var accepted = await trainer.PostAsJsonAsync($"/api/pt/sessions/{first.Id}/reschedule-request/decision",
            new PTRescheduleDecision(true, first.Date, first.StartTime, pending.Proposal.ExpiresAt));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        using var trainerAsks = await trainer.PostAsync($"/api/pt/sessions/{second.Id}/cancellation-request", null);
        var trainerPending = (await trainerAsks.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        using var declined = await member.PostAsJsonAsync($"/api/pt/sessions/{second.Id}/proposal/decision",
            new PTProposalDecision(false, trainerPending.Proposal!.ExpiresAt));
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);

        var after = await ContractOf(member, id);
        Assert.Equal("Cancelled", after.Sessions.Single(s => s.Id == first.Id).Status);
        Assert.Equal("Scheduled", after.Sessions.Single(s => s.Id == second.Id).Status);
        Assert.Equal(1, after.MakeUpUnits);
    }

    [Fact]
    public async Task FreezeRequestHoldsTheChosenSlotsAndWithdrawalReleasesThemWithoutUsingTheFreeze()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        var start = contract.StartDate;
        var freeze = new PTFreezeRequest(start, start.AddDays(7), "Travel");

        var preview = (await (await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes/preview", freeze))
            .Content.ReadFromJsonAsync<PTFreezeProposal>())!;
        Assert.Equal(3, preview.Sessions.Count);
        Assert.Empty(preview.SessionsNeedingSlot);
        Assert.All(preview.Sessions, s => Assert.True(s.Date >= start.AddDays(7)));

        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes", freeze);
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var created = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(3, await db.PTSessions.CountAsync(s => s.PTPurchaseId == id && s.SessionStatus == PTSessionStatus.FreezeHold));
            Assert.Equal(3, await db.PTSessions.CountAsync(s => s.PTPurchaseId == id && s.SessionStatus == PTSessionStatus.PendingFreeze));
        }

        using var withdrawn = await member.PostAsync($"/api/pt/freezes/{created.Id}/withdraw", null);
        Assert.True(withdrawn.StatusCode == HttpStatusCode.OK, await withdrawn.Content.ReadAsStringAsync());
        Assert.Equal("Withdrawn", (await withdrawn.Content.ReadFromJsonAsync<PTFreezeResponse>())!.Status);
        var after = await ContractOf(member, id);
        Assert.Equal(contract.Sessions.Count, after.Sessions.Count);
        Assert.All(after.Sessions, s => Assert.Equal("Scheduled", s.Status));
        Assert.False(after.FreezeUsed);
        using var again = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes", freeze);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task MemberCanMoveRemainingSessionsToASameLevelTrainerOnlyInsideTheChangeWindow()
    {
        using var member = Client("member", "Member");
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
        var slots = Enumerable.Range(1, 5).Select(day => new PTSlotInput((DayOfWeek)day, new TimeOnly(9, 0))).ToArray();
        using var published = await newTrainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(slots));
        Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var template = (await newTrainer.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/pt2/weekly-template"))!;
        var change = new PTTrainerChangeRequest("pt2", contract.StartDate.AddDays(7),
            template.Where(s => s.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday).Select(s => s.TemplateId).ToArray());

        using var closed = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/trainer-change", change);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.PTPurchases.SingleAsync(p => p.Id == id)).TrainerChangeDeadline = _clock.Now.UtcDateTime.AddDays(7);
            await db.SaveChangesAsync();
        }
        var trainers = (await member.GetFromJsonAsync<PTReplacementTrainer[]>($"/api/pt/contracts/{id}/replacement-trainers"))!;
        Assert.Equal("pt2", Assert.Single(trainers).UserId);
        using var changed = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/trainer-change", change);
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var after = (await changed.Content.ReadFromJsonAsync<PTContractResponse>())!;
        Assert.Equal("pt2", after.TrainerId);
        Assert.Equal(contract.TotalCredits, after.TotalCredits);
        Assert.Equal(contract.TotalCredits, after.ReservedCredits);
        Assert.Null(after.TrainerChangeDeadline);
        Assert.All(after.Sessions.Where(s => s.Status == "Scheduled"),
            s => Assert.True(s.Date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday));
    }
}
