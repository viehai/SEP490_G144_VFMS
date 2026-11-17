using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VFMS.API.Extensions;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Application.Services;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/pt")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PersonalTrainingController(PersonalTrainingService service, IWebHostEnvironment environment,
    IConfiguration configuration) : ControllerBase
{
    private bool DemoPaymentEnabled =>
        environment.IsDevelopment() && configuration.GetValue<bool>("PersonalTraining:EnableDemoPayment");

    private Task<IActionResult> AsUser<T>(Func<string, Task<Result<T>>> action) =>
        User.GetUserId() is { } actor ? Reply(action(actor)) : Task.FromResult<IActionResult>(Unauthorized());

    private async Task<IActionResult> Reply<T>(Task<Result<T>> result) => this.ToActionResult(await result);

    [HttpGet("trainers/{trainerId}/calendar")]
    [AllowAnonymous]
    public Task<IActionResult> Calendar(string trainerId, [FromQuery, BindRequired] DateOnly from, CancellationToken ct) =>
        Reply(service.Calendar(trainerId, from, ct));

    [HttpGet("trainers/{trainerId}/weekly-template")]
    [AllowAnonymous]
    public Task<IActionResult> WeeklyTemplate(string trainerId, CancellationToken ct) =>
        Reply(service.WeeklyTemplate(trainerId, ct));

    [HttpGet("availability")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Availability(CancellationToken ct) => AsUser(actor => service.Template(actor, ct));

    [HttpGet("availability/operating-hours")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> OperatingHours(CancellationToken ct) => AsUser(actor => service.OperatingHours(actor, ct));

    [HttpPut("availability")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Publish(PTPublishRequest request, CancellationToken ct) =>
        AsUser(actor => service.Publish(actor, request, ct));

    [HttpGet("packages")]
    [AllowAnonymous]
    public Task<IActionResult> Packages(CancellationToken ct) => Reply(service.Packages(false, ct));

    [HttpGet("manager/packages")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> ManagerPackages(CancellationToken ct) =>
        AsUser(actor => service.ManagerPackages(actor, ct));

    [HttpPost("manager/packages")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> CreatePackage(PTPackageRequest request, CancellationToken ct) =>
        AsUser(actor => service.SavePackage(actor, null, request, ct));

    [HttpPut("manager/packages/{id:int}")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> UpdatePackage(int id, PTPackageRequest request, CancellationToken ct) =>
        AsUser(actor => service.SavePackage(actor, id, request, ct));

    [HttpPatch("manager/packages/{id:int}/status")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> UpdatePackageStatus(int id, PTPackageStatusRequest request, CancellationToken ct) =>
        AsUser(actor => service.SetPackageSaleStatus(actor, id, request.IsActive!.Value, ct));

    [HttpDelete("manager/packages/{id:int}")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DeletePackage(int id, CancellationToken ct) =>
        AsUser(actor => service.DeletePackage(actor, id, ct));

    [HttpPost("schedule/preview")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Preview(PTBookingRequest request, CancellationToken ct) =>
        AsUser(actor => service.Preview(actor, request, ct));

    [HttpPost("schedule/booking-slots")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> BookingSlots(PTBookingSlotsRequest request, CancellationToken ct) =>
        AsUser(actor => service.BookingSlots(actor, request, ct));

    [HttpPost("purchases")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Checkout(PTBookingRequest request, CancellationToken ct) =>
        AsUser(actor => service.Checkout(actor, request, ct));

    [HttpPost("purchases/{orderNumber}/payment")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> StartPayment(string orderNumber, PTPaymentRequest request, CancellationToken ct) =>
        AsUser(actor => service.StartPayment(actor, orderNumber, request, ct));

    [HttpPost("purchases/{orderNumber}/demo-payment")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ConfirmDemoPayment(string orderNumber, CancellationToken ct) =>
        DemoPaymentEnabled
            ? AsUser(actor => service.ConfirmDemoPayment(actor, orderNumber, ct))
            : Task.FromResult<IActionResult>(NotFound());

    [HttpGet("purchases/{orderNumber}")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> CheckoutStatus(string orderNumber, CancellationToken ct) =>
        AsUser(actor => service.CheckoutStatus(actor, orderNumber, ct, DemoPaymentEnabled));

    [HttpGet("contracts")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> Contracts(CancellationToken ct) => AsUser(actor => service.MyContracts(actor, null, ct));

    [HttpGet("contracts/page")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ContractPage([FromQuery] int page, [FromQuery] string? trainerId, CancellationToken ct) =>
        AsUser(actor => service.ContractPage(actor, page, trainerId, ct));

    [HttpGet("contracts/{id:int}")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> Contract(int id, CancellationToken ct) => AsUser(actor => service.MyContracts(actor, id, ct));

    [HttpGet("assigned-members/page")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> AssignedPage([FromQuery] string? search, [FromQuery] bool historical, [FromQuery] int page,
        CancellationToken ct) => AsUser(actor => service.AssignedPage(actor, search, historical, page, ct));

    [HttpGet("teaching-schedule")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> TeachingSchedule([FromQuery, BindRequired] DateOnly from, CancellationToken ct) =>
        AsUser(actor => service.TeachingSchedule(actor, from, ct));

    [HttpPost("sessions/{id:int}/proposal")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Propose(int id, PTProposalRequest request, CancellationToken ct) =>
        AsUser(actor => service.Propose(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/proposal/decision")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> DecideProposal(int id, PTProposalDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideProposal(actor, id, request, ct));

    [HttpGet("sessions/{id:int}/reschedule-options")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> RescheduleOptions(int id, [FromQuery] DateOnly? from, CancellationToken ct) =>
        AsUser(actor => service.RescheduleOptions(actor, id, from, ct));

    [HttpPost("sessions/{id:int}/reschedule-request")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> RequestReschedule(int id, PTRescheduleRequest request, CancellationToken ct) =>
        AsUser(actor => service.RequestReschedule(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/reschedule-request/decision")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> DecideReschedule(int id, PTRescheduleDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideReschedule(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/attendance")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Attendance(int id, [FromBody] bool attended, CancellationToken ct) =>
        AsUser(actor => service.RecordAttendance(actor, id, attended, ct));

    [HttpPost("sessions/{id:int}/trainer-absence")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ReportTrainerAbsence(int id, PTTrainerAbsenceReportRequest request, CancellationToken ct) =>
        AsUser(actor => service.ReportTrainerAbsence(actor, id, request, ct));

    [HttpGet("manager/attendance-disputes")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> AttendanceDisputes([FromQuery] bool processed, [FromQuery] int page,
        CancellationToken ct) => AsUser(actor => service.AttendanceDisputes(actor, processed, page, ct));

    [HttpPost("manager/attendance-disputes/{id:int}/decision")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DecideDispute(int id, PTAttendanceDisputeDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideDispute(actor, id, request, ct));

    [HttpPut("sessions/{id:int}/note")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Note(int id, PTNoteRequest request, CancellationToken ct) =>
        AsUser(actor => service.Note(actor, id, request, ct));

    [HttpPost("contracts/{id:int}/freezes")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> RequestFreeze(int id, PTFreezeRequest request, CancellationToken ct) =>
        AsUser(actor => service.RequestFreeze(actor, id, request, ct));

    [HttpPost("contracts/{id:int}/freezes/preview")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> PreviewFreezeRequest(int id, PTFreezeRequest request, CancellationToken ct) =>
        AsUser(actor => service.PreviewFreezeRequest(actor, id, request, ct));

    [HttpPost("freezes/{id:int}/withdraw")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> WithdrawFreeze(int id, CancellationToken ct) =>
        AsUser(actor => service.WithdrawFreeze(actor, id, ct));

    [HttpGet("contracts/{id:int}/replacement-trainers")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ReplacementTrainers(int id, CancellationToken ct) =>
        AsUser(actor => service.ReplacementTrainers(actor, id, ct));

    [HttpPost("contracts/{id:int}/trainer-change/preview")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> PreviewTrainerChange(int id, PTTrainerChangeRequest request, CancellationToken ct) =>
        AsUser(actor => service.PreviewTrainerChange(actor, id, request, ct));

    [HttpPost("contracts/{id:int}/trainer-change")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ChangeTrainer(int id, PTTrainerChangeRequest request, CancellationToken ct) =>
        AsUser(actor => service.ChangeTrainer(actor, id, request, ct));

    [HttpPost("freezes/{id:int}/early-resume/preview")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> PreviewEarlyResume(int id, PTEarlyResumeRequest request, CancellationToken ct) =>
        AsUser(actor => service.PreviewEarlyResume(actor, id, request, ct));

    [HttpPost("freezes/{id:int}/early-resume/confirm")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> ConfirmEarlyResume(int id, PTEarlyResumeConfirmation request, CancellationToken ct) =>
        AsUser(actor => service.ConfirmEarlyResume(actor, id, request, ct));

    [HttpGet("manager/freezes/page")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> ManagerFreezePage([FromQuery] bool processed, [FromQuery] int page, CancellationToken ct) =>
        AsUser(actor => service.FreezePage(actor, true, processed, page, null, ct));

    [HttpGet("contracts/{id:int}/freezes")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> MemberFreezePage(int id, [FromQuery] int page, CancellationToken ct) =>
        AsUser(actor => service.FreezePage(actor, false, false, page, id, ct));

    [HttpGet("manager/freezes/{id:int}/preview")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> PreviewFreeze(int id, CancellationToken ct) =>
        AsUser(actor => service.PreviewFreeze(actor, id, ct));

    [HttpPost("manager/freezes/{id:int}/decision")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DecideFreeze(int id, PTFreezeDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideFreeze(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/make-up")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> ProposeMakeUp(int id, PTMakeUpRequest request, CancellationToken ct) =>
        AsUser(actor => service.ProposeMakeUp(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/make-up/decision")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> DecideMakeUpRequest(int id, PTProposalDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideMakeUpRequest(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/cancellation-request")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> RequestCancellation(int id, CancellationToken ct) =>
        AsUser(actor => service.RequestCancellation(actor, id, ct));

    [HttpPost("contracts/{id:int}/review")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Review(int id, PTReviewRequest request, CancellationToken ct) =>
        AsUser(actor => service.Review(actor, id, request, ct));
}
