using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;

namespace VFMS.API.Controllers;

/// <summary>
/// UC-71 Report Equipment Issue (Member), UC-72 Handle Equipment Issues (Staff),
/// UC-73 Manage Equipment Repairs (Manager, under /repair).
/// </summary>
[ApiController]
[Route("api/equipment-issues")]
[Authorize]
[Produces("application/json")]
public sealed class EquipmentIssuesController : ControllerBase
{
    private readonly IEquipmentIssueService _service;

    public EquipmentIssuesController(IEquipmentIssueService service)
    {
        _service = service;
    }

    // ── Member ───────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Roles = "Member")]
    [ProducesResponseType(typeof(EquipmentIssueResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Report([FromBody] ReportEquipmentIssueRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.ReportAsync(request, userId, ClientIp, cancellationToken));
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Member")]
    [ProducesResponseType(typeof(PagedResponse<EquipmentIssueResponse>), 200)]
    public async Task<IActionResult> GetMine([FromQuery] EquipmentIssueListRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.GetIssuesAsync(request, userId, cancellationToken));
    }

    // ── Staff & Manager ──────────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Staff,Manager")]
    [ProducesResponseType(typeof(PagedResponse<EquipmentIssueResponse>), 200)]
    public async Task<IActionResult> GetAll([FromQuery] EquipmentIssueListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetIssuesAsync(request, null, cancellationToken));

    /// <summary>Staff and Manager see any issue; a Member sees only their own.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Member,Staff,Manager")]
    [ProducesResponseType(typeof(EquipmentIssueResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        var onlyOwn = User.IsInRole("Staff") || User.IsInRole("Manager") ? null : userId;
        return this.ToActionResult(await _service.GetIssueAsync(id, onlyOwn, cancellationToken));
    }

    // ── Staff (UC-72) ────────────────────────────────────────────

    [HttpPost("{id:int}/start")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Start(int id, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.StartHandlingAsync(id, userId, ip, cancellationToken));

    [HttpPost("{id:int}/resolve")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Resolve(int id, [FromBody] EquipmentIssueActionRequest request, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.ResolveAsync(id, request.Note, userId, ip, cancellationToken));

    [HttpPost("{id:int}/escalate")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> Escalate(int id, [FromBody] EquipmentIssueActionRequest request, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.EscalateAsync(id, request.Note, userId, ip, cancellationToken));

    [HttpPost("{id:int}/mark-safe")]
    [Authorize(Roles = "Staff")]
    public Task<IActionResult> MarkSafe(int id, [FromBody] EquipmentIssueActionRequest request, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.MarkSafeAsync(id, request.Note, userId, ip, cancellationToken));

    // ── Manager repairs (UC-73) ──────────────────────────────────

    [HttpPost("{id:int}/repair/start")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> StartRepair(int id, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.StartRepairAsync(id, userId, ip, cancellationToken));

    [HttpPost("{id:int}/repair/confirm-safe")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> ConfirmSafe(int id, [FromBody] EquipmentIssueActionRequest request, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.ConfirmSafeAsync(id, request.Note, userId, ip, cancellationToken));

    [HttpPost("{id:int}/repair/not-safe")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> NotSafe(int id, [FromBody] EquipmentIssueActionRequest request, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.MarkNotSafeAsync(id, request.Note, userId, ip, cancellationToken));

    [HttpPost("{id:int}/repair/dispose")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> DisposeEquipment(int id, [FromBody] EquipmentIssueActionRequest request, CancellationToken cancellationToken) =>
        Run((userId, ip) => _service.DisposeAsync(id, request.Note, userId, ip, cancellationToken));

    private async Task<IActionResult> Run(Func<string, string?, Task<Result<EquipmentIssueResponse>>> action)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await action(userId, ClientIp));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
