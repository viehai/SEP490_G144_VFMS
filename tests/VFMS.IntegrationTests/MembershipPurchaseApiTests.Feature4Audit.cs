using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// Fixes from the UC 4.10-4.13 spec audit (2026-11-21).
public sealed partial class MembershipPurchaseApiTests
{
    private async Task CompleteContract(int id)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var contract = await db.PTPurchases.Include(p => p.Sessions).SingleAsync(p => p.Id == id);
        foreach (var session in contract.Sessions)
            session.SessionStatus = PTSessionStatus.Completed;
        contract.Status = PTPurchaseStatus.Completed;
        contract.IsCompleted = true;
        contract.RemainingSessions = 0;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CompletedContractIsReviewedOnceWithScreenedTextAndListedUnderCompletedContracts()
    {
        using var member = Client("member", "Member");
        var (id, _) = await ActivatedPTContract(member);
        var valid = new PTReviewRequest(4, "Huấn luyện viên rất nhiệt tình.");

        // AF Step 1: a contract that is not completed cannot be reviewed.
        Assert.False((await ContractOf(member, id)).CanReview);
        using var early = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/review", valid);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("PT_REVIEW_NOT_ELIGIBLE", await early.Content.ReadAsStringAsync());

        await CompleteContract(id);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.BannedWordLists.Add(new BannedWordList { Word = "badword", CreatedByUserId = "manager" });
            await db.SaveChangesAsync();
        }
        Assert.True((await ContractOf(member, id)).CanReview);
        var completed = (await member.GetFromJsonAsync<PagedResponse<PTContractResponse>>(
            "/api/pt/contracts/page?page=1&completed=true"))!.Items;
        Assert.Contains(completed, c => c.Id == id);
        Assert.DoesNotContain((await member.GetFromJsonAsync<PagedResponse<PTContractResponse>>(
            "/api/pt/contracts/page?page=1&completed=false"))!.Items, c => c.Id == id);

        // NF-4: 10 to 500 characters; AF Step 6 / GBR-66: banned words block the review and nothing is saved.
        using var shortText = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/review", new PTReviewRequest(4, "Tốt"));
        Assert.Equal(HttpStatusCode.BadRequest, shortText.StatusCode);
        using var banned = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/review",
            new PTReviewRequest(4, "This trainer is badword sometimes."));
        Assert.Equal(HttpStatusCode.BadRequest, banned.StatusCode);
        Assert.Contains("badword", await banned.Content.ReadAsStringAsync());

        using var saved = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/review", valid);
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        Assert.True((await ContractOf(member, id)).HasReview);
        // POST-1 / GBR-102: one published review per contract, for the trainer of the final session.
        using var again = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/review", valid);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var review = await db.TrainerReviews.SingleAsync(r => r.PTPurchaseId == id);
            Assert.Equal(("buyer", 4, true, TrainerReviewStatus.Approved),
                (review.TrainerUserId, review.Rating, review.IsVisible, review.ReviewStatus));
        }
    }

    [Fact]
    public async Task ExpiredContractCannotBeReviewed()
    {
        using var member = Client("member", "Member");
        var (id, _) = await ActivatedPTContract(member);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var contract = await db.PTPurchases.Include(p => p.Sessions).SingleAsync(p => p.Id == id);
            contract.EndDate = Today.AddDays(-30);
            foreach (var session in contract.Sessions.Take(1))
                session.SessionStatus = PTSessionStatus.AutoCompleted;
            await db.SaveChangesAsync();
        }
        var expired = await ContractOf(member, id);
        Assert.Equal(("Expired", false), (expired.Status, expired.CanReview));
        using var review = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/review",
            new PTReviewRequest(5, "Huấn luyện viên rất nhiệt tình."));
        Assert.Equal(HttpStatusCode.Conflict, review.StatusCode);
    }

    private async Task<PTSessionResponse> Disputed(HttpClient member, HttpClient trainer, PTSessionResponse session)
    {
        _clock.Now = LocalTime(session.Date, session.EndTime).AddMinutes(5);
        using var present = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/attendance", true);
        Assert.True(present.StatusCode == HttpStatusCode.OK, await present.Content.ReadAsStringAsync());
        using var reported = await member.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/trainer-absence",
            new PTTrainerAbsenceReportRequest("PT did not come"));
        var disputed = (await reported.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal("Disputed", disputed.Status);
        return disputed;
    }

    [Fact]
    public async Task ManagerDecidesHeldOrMemberAbsentOnceAndIsNamedOnTheProcessedList()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        var first = await Disputed(member, trainer, contract.Sessions[0]);
        var second = await Disputed(member, trainer, contract.Sessions[1]);

        using var absent = await manager.PostAsJsonAsync($"/api/pt/manager/attendance-disputes/{first.Id}/decision",
            new PTAttendanceDisputeDecision(PTDisputeDecisions.MemberAbsent, "Camera shows no member"));
        Assert.Equal("NoShow", (await absent.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        using var held = await manager.PostAsJsonAsync($"/api/pt/manager/attendance-disputes/{second.Id}/decision",
            new PTAttendanceDisputeDecision(PTDisputeDecisions.SessionHeld, "Check-in shows both"));
        Assert.Equal("Completed", (await held.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        Assert.Equal(2, (await ContractOf(member, id)).UsedCredits);

        // POST-1: a decided session keeps its final status.
        using var twice = await manager.PostAsJsonAsync($"/api/pt/manager/attendance-disputes/{second.Id}/decision",
            new PTAttendanceDisputeDecision(PTDisputeDecisions.PTAbsent, "Changed my mind"));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        // NF-5: the decision is saved with the Manager, shown on the processed list.
        var processed = (await manager.GetFromJsonAsync<PagedResponse<PTAttendanceDispute>>(
            "/api/pt/manager/attendance-disputes?processed=true&page=1"))!.Items;
        Assert.Equal(2, processed.Count);
        Assert.All(processed, row => Assert.Equal("manager", row.ReviewerName));
        Assert.Contains(processed, row => row.SessionId == first.Id && row.Decision == PTDisputeDecisions.MemberAbsent);
    }

    [Fact]
    public async Task MemberAbsentDecisionKeepsAPendingFreezeSessionFree()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var session = await Disputed(member, trainer, contract.Sessions[0]);

        // GBR-97: a session flagged 'Pending Freeze' is never forfeited, also through a dispute.
        using var absent = await manager.PostAsJsonAsync($"/api/pt/manager/attendance-disputes/{session.Id}/decision",
            new PTAttendanceDisputeDecision(PTDisputeDecisions.MemberAbsent, "Member was away"));
        Assert.True(absent.StatusCode == HttpStatusCode.OK, await absent.Content.ReadAsStringAsync());
        Assert.Equal("PendingFreeze", (await absent.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        Assert.Equal(0, (await ContractOf(member, id)).UsedCredits);
    }

    [Fact]
    public async Task SixtyNotAcceptingDaysCountFromTheEndOfTheLastRunningContract()
    {
        using var member = Client("member", "Member");
        var (id, _) = await ActivatedPTContract(member);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var profile = await db.TrainerProfiles.SingleAsync(p => p.PTUserId == "buyer");
        profile.AcceptingNewMembers = false;
        profile.NotAcceptingSince = Today.AddDays(-90);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<PersonalTrainingService>();
        async Task<TrainerProfile> Swept(DateOnly day)
        {
            await service.ExpireInactiveTrainers(day, default);
            return await db.TrainerProfiles.AsNoTracking().SingleAsync(p => p.PTUserId == "buyer");
        }

        // GBR-98: 90 days 'Not Accepting', but the contract is still running, so the 60 days have not started.
        var running = await Swept(Today);
        Assert.Equal((true, Today), (running.IsActive, running.NotAcceptingCountFrom));

        await CompleteContract(id);
        Assert.True((await Swept(Today.AddDays(60))).IsActive);
        Assert.False((await Swept(Today.AddDays(61))).IsActive);
    }
}
