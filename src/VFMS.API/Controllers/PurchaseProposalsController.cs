using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Procurement;

namespace VFMS.API.Controllers;

/// <summary>UC-74 Manage Purchase Proposals (Staff) and UC-75 Approve Purchase Request (Manager).</summary>
[ApiController]
[Route("api/purchase-proposals")]
[Authorize]
[Produces("application/json")]
public sealed class PurchaseProposalsController : ControllerBase
{
    private readonly IPurchaseProposalService _service;

    public PurchaseProposalsController(IPurchaseProposalService service)
    {
        _service = service;
    }

    // ── Staff (UC-74) ────────────────────────────────────────────

    /// <summary>The current Staff member's own proposals, including drafts.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(PagedResponse<PurchaseProposalResponse>), 200)]
    public Task<IActionResult> GetMine([FromQuery] PurchaseProposalListRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.GetMyProposalsAsync(request, userId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(PurchaseProposalResponse), 201)]
    [ProducesResponseType(400)]
    public Task<IActionResult> Create([FromBody] PurchaseProposalRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.CreateAsync(request, userId, ClientIp, cancellationToken));

    /// <summary>Edit a Draft (including one returned for revision). Submit = true sends it again.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(PurchaseProposalResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Update(int id, [FromBody] PurchaseProposalRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.UpdateDraftAsync(id, request, userId, ClientIp, cancellationToken));

    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(PurchaseProposalResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Submit(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.SubmitAsync(id, userId, ClientIp, cancellationToken));

    /// <summary>Staff see only their own proposals; the Manager sees any sent proposal.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Staff,Manager")]
    [ProducesResponseType(typeof(PurchaseProposalResponse), 200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.GetAsync(id, User.IsInRole("Manager") ? null : userId, cancellationToken));

    // ── Manager (UC-75) ──────────────────────────────────────────

    /// <summary>Sent proposals, Submitted (pending) first. Drafts are not shown.</summary>
    [HttpGet]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(PagedResponse<PurchaseProposalResponse>), 200)]
    public async Task<IActionResult> GetForReview([FromQuery] PurchaseProposalListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetForReviewAsync(request, cancellationToken));

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> Approve(int id, [FromBody] PurchaseProposalReviewRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.ApproveAsync(id, request.Note, userId, ClientIp, cancellationToken));

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> Reject(int id, [FromBody] PurchaseProposalReviewRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.RejectAsync(id, request.Note, userId, ClientIp, cancellationToken));

    [HttpPost("{id:int}/request-revision")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> RequestRevision(int id, [FromBody] PurchaseProposalReviewRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.RequestRevisionAsync(id, request.Note, userId, ClientIp, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = "Manager")]
    public Task<IActionResult> Complete(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.CompleteAsync(id, userId, ClientIp, cancellationToken));

    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await action(userId));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
