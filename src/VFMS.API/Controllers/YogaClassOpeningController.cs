using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Yoga;

namespace VFMS.API.Controllers;

/// <summary>
/// Class opening requests (GBR-128..GBR-130): the Yoga Instructor asks to open a class, the Manager approves
/// (creating the Draft class from the reviewed form) or rejects with a reason.
/// </summary>
[ApiController]
[Route("api/yoga/class-requests")]
[Authorize]
[Produces("application/json")]
public sealed class YogaClassOpeningController : ControllerBase
{
    private const string Instructor = YogaClassService.InstructorRole;
    private readonly IYogaClassOpeningService _service;

    public YogaClassOpeningController(IYogaClassOpeningService service) => _service = service;

    // ── Yoga Instructor ──────────────────────────────────────────

    /// <summary>Class types and active studios for the request form.</summary>
    [HttpGet("options")]
    [Authorize(Roles = Instructor)]
    public async Task<IActionResult> GetOptions(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetOptionsAsync(cancellationToken));

    [HttpGet("mine")]
    [Authorize(Roles = Instructor)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.GetMineAsync(userId, cancellationToken))
            : Unauthorized();

    [HttpPost]
    [Authorize(Roles = Instructor)]
    public async Task<IActionResult> Submit([FromBody] YogaClassOpeningRequestRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.SubmitAsync(userId, request, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("{id:int}/withdraw")]
    [Authorize(Roles = Instructor)]
    public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.WithdrawAsync(userId, id, ClientIp, cancellationToken))
            : Unauthorized();

    // ── Manager ──────────────────────────────────────────────────

    /// <summary>All requests, newest first; status = Pending / Approved / Rejected / Withdrawn.</summary>
    [HttpGet]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> GetAll([FromQuery] string? status, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetAllAsync(status, cancellationToken));

    /// <summary>Body: the create-class form as the Manager reviewed it. Creates the Draft class and approves the request.</summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Approve(int id, [FromBody] YogaClassRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.ApproveAsync(id, request, userId, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectYogaClassOpeningRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.RejectAsync(id, request.Reason, userId, ClientIp, cancellationToken))
            : Unauthorized();

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
