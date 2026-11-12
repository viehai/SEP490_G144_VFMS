using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VFMS.API.Extensions;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Infrastructure.PersonalTraining;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/pt")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PersonalTrainingController(PersonalTrainingService service) : ControllerBase
{
    private Task<IActionResult> AsUser<T>(Func<string, Task<Result<T>>> action) =>
        User.GetUserId() is { } actor ? Reply(action(actor)) : Task.FromResult<IActionResult>(Unauthorized());

    private async Task<IActionResult> Reply<T>(Task<Result<T>> result) => this.ToActionResult(await result);

    [HttpGet("trainers/{trainerId}/calendar")]
    [AllowAnonymous]
    public Task<IActionResult> Calendar(string trainerId, [FromQuery, BindRequired] DateOnly from, CancellationToken ct) =>
        Reply(service.Calendar(trainerId, from, ct));

    [HttpGet("availability")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Availability(CancellationToken ct) => AsUser(actor => service.Template(actor, ct));

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

    [HttpPost("schedule/preview")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Preview(PTBookingRequest request, CancellationToken ct) =>
        AsUser(actor => service.Preview(actor, request, ct));

    [HttpPost("purchases")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Checkout(PTBookingRequest request, CancellationToken ct) =>
        AsUser(actor => service.Checkout(actor, request, ct));

    [HttpPost("purchases/{orderNumber}/payment")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> StartPayment(string orderNumber, PTPaymentRequest request, CancellationToken ct) =>
        AsUser(actor => service.StartPayment(actor, orderNumber, request, ct));

    [HttpGet("purchases/{orderNumber}")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> CheckoutStatus(string orderNumber, CancellationToken ct) =>
        AsUser(actor => service.CheckoutStatus(actor, orderNumber, ct));

    [HttpGet("contracts")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> Contracts(CancellationToken ct) => AsUser(actor => service.MyContracts(actor, null, ct));

    [HttpGet("contracts/{id:int}")]
    [Authorize(Roles = "Member,PT")]
    public Task<IActionResult> Contract(int id, CancellationToken ct) => AsUser(actor => service.MyContracts(actor, id, ct));

    [HttpGet("assigned-members")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Assigned([FromQuery] string? search, CancellationToken ct) =>
        AsUser(actor => service.Assigned(actor, search, ct));

    [HttpPost("sessions/{id:int}/proposal")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Propose(int id, PTProposalRequest request, CancellationToken ct) =>
        AsUser(actor => service.Propose(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/proposal/decision")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> DecideProposal(int id, PTProposalDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideProposal(actor, id, request, ct));

    [HttpGet("sessions/{id:int}/reschedule-options")]
    [Authorize(Roles = "Member")]
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

    [HttpPut("sessions/{id:int}/note")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> Note(int id, PTNoteRequest request, CancellationToken ct) =>
        AsUser(actor => service.Note(actor, id, request, ct));

    [HttpPost("contracts/{id:int}/freezes")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> RequestFreeze(int id, PTFreezeRequest request, CancellationToken ct) =>
        AsUser(actor => service.RequestFreeze(actor, id, request, ct));

    [HttpGet("freezes")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Freezes(CancellationToken ct) => AsUser(actor => service.Freezes(actor, false, ct));

    [HttpGet("manager/freezes")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> ManagerFreezes(CancellationToken ct) => AsUser(actor => service.Freezes(actor, true, ct));

    [HttpGet("manager/freezes/{id:int}/preview")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> PreviewFreeze(int id, CancellationToken ct) =>
        AsUser(actor => service.PreviewFreeze(actor, id, ct));

    [HttpPost("manager/freezes/{id:int}/decision")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DecideFreeze(int id, PTFreezeDecision request, CancellationToken ct) =>
        AsUser(actor => service.DecideFreeze(actor, id, request, ct));

    [HttpPost("sessions/{id:int}/make-up")]
    [Authorize(Roles = "PT")]
    public Task<IActionResult> ProposeMakeUp(int id, PTMakeUpRequest request, CancellationToken ct) =>
        AsUser(actor => service.ProposeMakeUp(actor, id, request, ct));

    [HttpPost("contracts/{id:int}/review")]
    [Authorize(Roles = "Member")]
    public Task<IActionResult> Review(int id, PTReviewRequest request, CancellationToken ct) =>
        AsUser(actor => service.Review(actor, id, request, ct));
}
