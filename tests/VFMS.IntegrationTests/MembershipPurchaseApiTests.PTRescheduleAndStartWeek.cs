using System.Net;
using System.Net.Http.Json;
using VFMS.Contracts.PersonalTraining;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task PTSchedulePolicyRejectsAnUnpublishedOrdinaryTrainerProposal()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var original = contract.Sessions[0];
        var unpublished = new TimeOnly(10, 0);
        var templates = (await trainer.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/buyer/weekly-template"))!;
        Assert.DoesNotContain(templates, slot => slot.DayOfWeek == original.Date.DayOfWeek && slot.StartTime == unpublished);

        using var response = await trainer.PostAsJsonAsync($"/api/pt/sessions/{original.Id}/proposal",
            new PTProposalRequest(original.Date, unpublished, "Reschedule"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await ContractOf(member, id);
        var unchanged = Assert.Single(after.Sessions, session => session.Id == original.Id);
        Assert.Equal((original.Date, original.StartTime, "Scheduled"), (unchanged.Date, unchanged.StartTime, unchanged.Status));
        Assert.Null(unchanged.Proposal);
        Assert.Equal(contract.ReservedCredits, after.ReservedCredits);
        Assert.Equal(0, after.UsedCredits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PTSchedulePolicyRechecksThePublishedSlotWhenMemberAccepts(bool removeBeforeAcceptance)
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var original = contract.Sessions[0];
        var target = new TimeOnly(9, 0);
        using var proposed = await trainer.PostAsJsonAsync($"/api/pt/sessions/{original.Id}/proposal",
            new PTProposalRequest(original.Date, target, "Reschedule"));
        Assert.True(proposed.StatusCode == HttpStatusCode.OK, await proposed.Content.ReadAsStringAsync());
        var pending = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;

        if (removeBeforeAcceptance)
        {
            var template = (await trainer.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/buyer/weekly-template"))!;
            using var published = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(template
                .Where(slot => slot.DayOfWeek != original.Date.DayOfWeek || slot.StartTime != target)
                .Select(slot => new PTSlotInput(slot.DayOfWeek, slot.StartTime)).ToArray()));
            Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        }

        using var accepted = await member.PostAsJsonAsync($"/api/pt/sessions/{original.Id}/proposal/decision",
            new PTProposalDecision(true, pending.Proposal!.ExpiresAt));

        Assert.Equal(removeBeforeAcceptance ? HttpStatusCode.Conflict : HttpStatusCode.OK, accepted.StatusCode);
        var after = await ContractOf(member, id);
        var result = Assert.Single(after.Sessions, session => session.Id == original.Id);
        Assert.Equal((original.Date, removeBeforeAcceptance ? original.StartTime : target, "Scheduled"),
            (result.Date, result.StartTime, result.Status));
        Assert.Null(result.Proposal);
        Assert.Equal(contract.ReservedCredits, after.ReservedCredits);
        Assert.Equal(0, after.UsedCredits);
    }

    [Fact]
    public async Task PTSchedulePolicyStillAllowsAMakeUpOutsidePublishedSlots()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var original = contract.Sessions[0];
        using var cancellation = await trainer.PostAsJsonAsync($"/api/pt/sessions/{original.Id}/cancellation-request", new { });
        Assert.True(cancellation.StatusCode == HttpStatusCode.OK, await cancellation.Content.ReadAsStringAsync());
        var pendingCancellation = (await cancellation.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        using var cancelled = await member.PostAsJsonAsync($"/api/pt/sessions/{original.Id}/proposal/decision",
            new PTProposalDecision(true, pendingCancellation.Proposal!.ExpiresAt));
        Assert.True(cancelled.StatusCode == HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());

        var target = new TimeOnly(10, 0);
        var templates = (await trainer.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/buyer/weekly-template"))!;
        Assert.DoesNotContain(templates, slot => slot.DayOfWeek == original.Date.DayOfWeek && slot.StartTime == target);
        using var proposed = await trainer.PostAsJsonAsync($"/api/pt/sessions/{original.Id}/make-up",
            new PTMakeUpRequest(original.Date, target));
        Assert.True(proposed.StatusCode == HttpStatusCode.Created, await proposed.Content.ReadAsStringAsync());
        var pending = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        using var accepted = await member.PostAsJsonAsync($"/api/pt/sessions/{pending.Id}/proposal/decision",
            new PTProposalDecision(true, pending.Proposal!.ExpiresAt));

        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var after = await ContractOf(member, id);
        var replacement = Assert.Single(after.Sessions, session => session.Id == pending.Id);
        Assert.Equal((original.Date, target, "Scheduled"), (replacement.Date, replacement.StartTime, replacement.Status));
        Assert.Equal(contract.ReservedCredits, after.ReservedCredits);
        Assert.Equal(0, after.UsedCredits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PTSchedulePolicySkipsNextMondayBusyAfterAWednesdayStart(bool deferFirstOccurrence)
    {
        // Tuesday 18:00 in Vietnam: Wednesday's first occurrence is exactly 24 hours away.
        _clock.Now = new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero).AddSeconds(deferFirstOccurrence ? 1 : 0);
        var request = await PreparePTCheckout(4);
        using var member = Client("member", "Member");
        var template = (await member.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/buyer/weekly-template"))!;
        var pattern = template.Where(slot => slot.StartTime.Hour == 18 &&
            slot.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday).Select(slot => slot.TemplateId).ToArray();
        request = request with { StartDate = new(2026, 12, 2), TemplateIds = pattern };
        await AddOtherMemberSession(new(0, new(2026, 12, 7), new(18, 0), new(19, 0), "Scheduled"));

        using var choicesResponse = await member.PostAsJsonAsync("/api/pt/schedule/booking-slots",
            new PTBookingSlotsRequest(request.TrainerId, request.PackageId, request.StartDate, pattern));
        Assert.Equal(HttpStatusCode.OK, choicesResponse.StatusCode);
        var choices = (await choicesResponse.Content.ReadFromJsonAsync<PTWeeklySlot[]>())!;
        Assert.All(choices.Where(slot => pattern.Contains(slot.TemplateId)), slot => Assert.Null(slot.BlockedReason));

        using var previewResponse = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        DateOnly[] expected = deferFirstOccurrence
            ? [new(2026, 12, 9), new(2026, 12, 14), new(2026, 12, 16), new(2026, 12, 21)]
            : [new(2026, 12, 2), new(2026, 12, 9), new(2026, 12, 14), new(2026, 12, 16)];
        Assert.Equal(expected, preview.Sessions.Select(session => session.Date));
        Assert.Equal(new DateOnly(2026, 12, 7), Assert.Single(preview.Skipped).Date);
        Assert.Equal(deferFirstOccurrence ? 1 : 0, preview.Deferred.Count);
        Assert.Equal(expected[^1], preview.ExpiresOn);
        Assert.All(preview.Sessions, session => Assert.True(LocalTime(session.Date, session.StartTime) >= _clock.Now.AddHours(24)));

        using var held = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(held.StatusCode == HttpStatusCode.Created, await held.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PTSchedulePolicyStillBlocksABusySlotInTheWednesdayStartCalendarWeek()
    {
        _clock.Now = new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero);
        var request = await PreparePTCheckout(4);
        using var member = Client("member", "Member");
        var template = (await member.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/buyer/weekly-template"))!;
        var pattern = template.Where(slot => slot.StartTime.Hour == 18 &&
            slot.DayOfWeek is DayOfWeek.Wednesday or DayOfWeek.Friday).Select(slot => slot.TemplateId).ToArray();
        request = request with { StartDate = new(2026, 12, 2), TemplateIds = pattern };
        await AddOtherMemberSession(new(0, new(2026, 12, 4), new(18, 0), new(19, 0), "Scheduled"));

        using var held = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        var contracts = (await member.GetFromJsonAsync<PTContractResponse[]>("/api/pt/contracts"))!;
        Assert.Empty(contracts);
    }
}
