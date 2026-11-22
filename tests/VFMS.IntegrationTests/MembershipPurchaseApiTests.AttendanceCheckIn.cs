using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// UC 4.6 / UC 4.11 / GBR-103 after GBR-92: check-in 'Present', the PT's response period and the dispute check-in view.
public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task APTAbsentRecordAgainstAMemberReportGoesToTheManager()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var manager = Client("manager", "Manager");
        var (_, contract) = await ActivatedPTContract(member);
        var session = contract.Sessions[0];

        _clock.Now = LocalTime(session.Date, session.EndTime).AddMinutes(5);
        using var absent = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/attendance", false);
        Assert.True(absent.StatusCode == HttpStatusCode.OK, await absent.Content.ReadAsStringAsync());
        using var reported = await member.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/trainer-absence",
            new PTTrainerAbsenceReportRequest("I was there"));
        Assert.Equal("Disputed", (await reported.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);

        var row = Assert.Single((await manager.GetFromJsonAsync<PagedResponse<PTAttendanceDispute>>(
            "/api/pt/manager/attendance-disputes?processed=false&page=1"))!.Items);
        Assert.Equal((session.Id, false), (row.SessionId, row.TrainerRecordedPresent));
        Assert.NotNull(row.TrainerRecordedAt);
    }

    [Fact]
    public async Task ThePTCanAnswerAReportUntilTheEndOfTheNextDay()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var answered = contract.Sessions[0];
        var late = contract.Sessions[^1];

        // Next day: the PT says the session was taught, so it becomes a dispute instead of a make-up unit.
        _clock.Now = LocalTime(answered.Date, answered.EndTime).AddMinutes(5);
        using var first = await member.PostAsJsonAsync($"/api/pt/sessions/{answered.Id}/trainer-absence", new PTTrainerAbsenceReportRequest());
        Assert.Equal("Cancelled", (await first.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);
        _clock.Now = LocalTime(answered.Date.AddDays(1), new TimeOnly(21, 0));
        using var present = await trainer.PostAsJsonAsync($"/api/pt/sessions/{answered.Id}/attendance", true);
        Assert.True(present.StatusCode == HttpStatusCode.OK, await present.Content.ReadAsStringAsync());
        Assert.Equal("Disputed", (await present.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);

        // No answer by the end of the next day: the make-up unit stays and becomes usable.
        _clock.Now = LocalTime(late.Date, late.EndTime).AddMinutes(5);
        using var second = await member.PostAsJsonAsync($"/api/pt/sessions/{late.Id}/trainer-absence", new PTTrainerAbsenceReportRequest());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        _clock.Now = LocalTime(late.Date.AddDays(2), new TimeOnly(1, 0));
        using var tooLate = await trainer.PostAsJsonAsync($"/api/pt/sessions/{late.Id}/attendance", true);
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Equal(1, (await ContractOf(member, id)).MakeUpUnits);
    }

    [Fact]
    public async Task ACheckInPresentIsNotAPTRecordAndTheManagerSeesTheMembersVisits()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var manager = Client("manager", "Manager");
        var (_, contract) = await ActivatedPTContract(member);
        var session = contract.Sessions[0];
        var start = LocalTime(session.Date, session.StartTime).UtcDateTime;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var stored = await db.PTSessions.SingleAsync(s => s.Id == session.Id);
            stored.SessionStatus = PTSessionStatus.Completed;   // UC 4.6 step 7, as the kiosk sets it
            stored.CheckInCompletedAt = start.AddMinutes(-10);
            db.CheckIns.Add(new CheckIn
            {
                MemberUserId = "member", CheckinTime = start.AddMinutes(-10), CheckinMethod = CheckInMethod.QRCode,
                CheckoutTime = start.AddMinutes(70), CheckoutMethod = CheckInMethod.QRCode, Status = CheckInStatus.CheckedOut
            });
            db.CheckIns.Add(new CheckIn   // another day: not shown
            {
                MemberUserId = "member", CheckinTime = start.AddDays(-1), CheckinMethod = CheckInMethod.FaceRecognition,
                CheckoutTime = start.AddDays(-1).AddHours(1), Status = CheckInStatus.CheckedOut
            });
            await db.SaveChangesAsync();
        }
        Assert.True((await ContractOf(member, contract.Id)).Sessions.Single(s => s.Id == session.Id).CompletedByCheckIn);

        // GBR-103: only a check-in 'Present' and no PT record, so the report gives a make-up unit, not a dispute.
        _clock.Now = LocalTime(session.Date, session.EndTime).AddMinutes(5);
        using var reported = await member.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/trainer-absence", new PTTrainerAbsenceReportRequest());
        Assert.Equal("Cancelled", (await reported.Content.ReadFromJsonAsync<PTSessionResponse>())!.Status);

        // UC 4.11 step 3: the Member's visits on the session day, matched to the session.
        var visit = Assert.Single((await manager.GetFromJsonAsync<PTDisputeCheckIn[]>(
            $"/api/pt/manager/attendance-disputes/{session.Id}/check-ins"))!);
        Assert.Equal(("QRCode", true, true), (visit.CheckinMethod, visit.InCheckInWindow, visit.OverlapsSession));
        using var forbidden = await member.GetAsync($"/api/pt/manager/attendance-disputes/{session.Id}/check-ins");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
