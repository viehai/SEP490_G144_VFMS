using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// PT attendance, facility visits and the trainer's response to a member report.
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

        // UC 4.11: the Manager reviews the record as it was; the PT cannot change it while disputed.
        using var changed = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/attendance", true);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.False(Assert.Single((await manager.GetFromJsonAsync<PagedResponse<PTAttendanceDispute>>(
            "/api/pt/manager/attendance-disputes?processed=false&page=1"))!.Items).TrainerRecordedPresent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FacilityEntryAndExitDuringPtAppointmentLeaveAttendanceAndCreditsUntouched(bool kiosk)
    {
        using var member = Client("member", "Member");
        using var staff = Client("staff", "Staff");
        var (id, contract) = await ActivatedPTContract(member);
        var session = contract.Sessions[0];
        _clock.Now = LocalTime(session.Date, session.StartTime);
        var qr = await member.GetFromJsonAsync<MemberQrResponse>("/api/my-qr");
        var scan = new KioskScanRequest(null, qr!.Token);
        using var entered = kiosk
            ? await staff.PostAsJsonAsync("/api/kiosk/check-in", scan)
            : await staff.PostAsJsonAsync("/api/check-ins/manual", new ManualCheckInRequest("member"));
        Assert.True(entered.StatusCode == HttpStatusCode.Created, await entered.Content.ReadAsStringAsync());
        var entry = (await entered.Content.ReadFromJsonAsync<CheckInResultResponse>())!;
        Assert.Null(entry.CompletedPtSessionId);
        using var exited = kiosk
            ? await staff.PostAsJsonAsync("/api/kiosk/check-out", scan)
            : await staff.PostAsync($"/api/check-ins/{entry.CheckInId}/check-out", null);
        Assert.Equal(HttpStatusCode.OK, exited.StatusCode);

        var after = await ContractOf(member, id);
        Assert.Equal("Scheduled", after.Sessions.Single(s => s.Id == session.Id).Status);
        Assert.Equal((contract.UsedCredits, contract.RemainingCredits), (after.UsedCredits, after.RemainingCredits));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.PTSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(stored.CheckInCompletedAt);
        Assert.Null(stored.AttendanceRecordedAt);
        Assert.Equal(CheckInStatus.CheckedOut, (await db.CheckIns.SingleAsync(c => c.Id == entry.CheckInId)).Status);
    }

    [Fact]
    public async Task OnlyTheTrainerAssignedToTheSessionCanRecordLateAttendanceAfterAContractTransfer()
    {
        using var member = Client("member", "Member");
        using var original = Client("buyer", "PT");
        using var successor = Client("gift", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        var session = contract.Sessions[0];
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.ApplicationUserRoles.Add(new ApplicationUserRole
            {
                UserId = "gift", Role = await db.ApplicationRoles.SingleAsync(r => r.RoleName == "PT")
            });
            (await db.PTPurchases.SingleAsync(p => p.Id == id)).PTUserId = "gift";
            await db.SaveChangesAsync();
        }
        _clock.Now = LocalTime(session.Date.AddDays(2), new TimeOnly(10, 0));
        var detail = $"/api/pt/contracts/{id}/teaching-sessions/{session.Id}";
        var attendance = $"/api/pt/sessions/{session.Id}/attendance";
        Assert.Equal(HttpStatusCode.NotFound, (await original.GetAsync($"/api/pt/contracts/{id}")).StatusCode);
        Assert.True((await original.GetFromJsonAsync<PTSessionResponse>(detail))!.CanRecordAttendance);
        Assert.Equal(HttpStatusCode.NotFound, (await successor.GetAsync(detail)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await successor.PostAsJsonAsync(attendance, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await original.PostAsJsonAsync(attendance, true)).StatusCode);
        Assert.Equal(1, (await ContractOf(member, id)).UsedCredits);
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
    public async Task HistoricalKioskAttendanceIsNotATrainerRecordForDisputes()
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
            stored.SessionStatus = PTSessionStatus.Completed;   // Historical data from before trainer-only attendance.
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
