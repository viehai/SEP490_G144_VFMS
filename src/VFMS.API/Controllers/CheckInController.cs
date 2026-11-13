using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.CheckIn;

namespace VFMS.API.Controllers;

/// <summary>UC-43/UC-52 Manage Face Recognition Profile (Customer, Member).</summary>
[ApiController]
[Route("api/face-profile")]
[Authorize(Roles = "Customer,Member")]
[Produces("application/json")]
public sealed class FaceProfileController : ControllerBase
{
    private readonly IFaceProfileService _service;

    public FaceProfileController(IFaceProfileService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.GetAsync(userId, cancellationToken)) : Unauthorized();

    [HttpPut]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Register([FromBody] RegisterFaceRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.RegisterAsync(userId, request, ClientIp, cancellationToken))
            : Unauthorized();

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await _service.DeleteAsync(userId, ClientIp, cancellationToken))
            : Unauthorized();

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>The member's short-lived QR code for the kiosk and yoga attendance.</summary>
[ApiController]
[Route("api/my-qr")]
[Authorize(Roles = "Customer,Member")]
[Produces("application/json")]
public sealed class MemberQrController : ControllerBase
{
    private readonly IMemberQrService _service;

    public MemberQrController(IMemberQrService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Issue(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId ? this.ToActionResult(await _service.IssueAsync(userId, cancellationToken)) : Unauthorized();
}

/// <summary>
/// UC-44/UC-45 entrance and exit kiosk. Staff and Manager can check anyone in or out. A Customer or Member
/// can only scan themselves: an open face-match would let anyone probe biometrics or check other people in.
/// </summary>
[ApiController]
[Route("api/kiosk")]
[Authorize(Roles = "Staff,Manager,Customer,Member")]
[Produces("application/json")]
public sealed class KioskController : ControllerBase
{
    private readonly ICheckInService _service;

    public KioskController(ICheckInService service) => _service = service;

    [HttpPost("check-in")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> CheckIn([FromBody] KioskScanRequest request, CancellationToken cancellationToken) =>
        Scope() is var (ok, self) && ok
            ? this.ToActionResult(await _service.KioskCheckInAsync(request, self, cancellationToken))
            : Unauthorized();

    [HttpPost("check-out")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> CheckOut([FromBody] KioskScanRequest request, CancellationToken cancellationToken) =>
        Scope() is var (ok, self) && ok
            ? this.ToActionResult(await _service.KioskCheckOutAsync(request, self, cancellationToken))
            : Unauthorized();

    /// <summary>Staff/Manager: no restriction. Others: limited to their own user ID, which must exist.</summary>
    private (bool Ok, string? Self) Scope() =>
        User.IsInRole("Staff") || User.IsInRole("Manager") ? (true, null)
        : User.GetUserId() is { } id ? (true, id) : (false, null);
}

/// <summary>UC-48/UC-57 View Active Check-ins (Staff, Manager).</summary>
[ApiController]
[Route("api/check-ins")]
[Authorize(Roles = "Staff,Manager")]
[Produces("application/json")]
public sealed class CheckInsController : ControllerBase
{
    private readonly IActiveCheckInService _service;
    private readonly ICheckInService _checkIns;

    public CheckInsController(IActiveCheckInService service, ICheckInService checkIns)
    {
        _service = service;
        _checkIns = checkIns;
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive([FromQuery] string? search, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetActiveAsync(search, cancellationToken));

    /// <summary>UC-46 Front Desk Check-in: find the member.</summary>
    [HttpGet("members")]
    public async Task<IActionResult> SearchMembers([FromQuery] string? search, CancellationToken cancellationToken) =>
        this.ToActionResult(await _checkIns.SearchMembersAsync(search, cancellationToken));

    /// <summary>UC-46 Manual Check-In, recorded with the staff member's ID.</summary>
    [HttpPost("manual")]
    public async Task<IActionResult> ManualCheckIn([FromBody] ManualCheckInRequest request, CancellationToken cancellationToken) =>
        User.GetUserId() is { } staffId
            ? this.ToActionResult(await _checkIns.ManualCheckInAsync(request.MemberUserId, staffId, cancellationToken))
            : Unauthorized();

    /// <summary>UC-47 Manual Check-Out from Active Check-ins, recorded with the staff member's ID.</summary>
    [HttpPost("{id:int}/check-out")]
    public async Task<IActionResult> ManualCheckOut(int id, CancellationToken cancellationToken) =>
        User.GetUserId() is { } staffId
            ? this.ToActionResult(await _checkIns.ManualCheckOutAsync(id, staffId, cancellationToken))
            : Unauthorized();
}
