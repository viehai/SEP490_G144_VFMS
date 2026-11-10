using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Competitions;

namespace VFMS.API.Controllers;

/// <summary>
/// Module 6 – Competitions.
/// UC-56 View Competitions, UC-57 Competition Registration, UC-58 View Competition History,
/// UC-59 Manage Competitions, UC-60 Manage Competition Participants,
/// UC-61 Manage Competition Results, UC-62 Review and Confirm Competition Results.
/// </summary>
[ApiController]
[Route("api/competitions")]
[Authorize]
[Produces("application/json")]
public sealed class CompetitionsController : ControllerBase
{
    private const string MemberRoles = "Member";
    private const string StaffRoles = "Staff,Manager";
    private const string ManagerRoles = "Manager";

    private readonly ICompetitionService _service;

    public CompetitionsController(ICompetitionService service)
    {
        _service = service;
    }

    // ── UC-56 View Competitions ──────────────────────────────────

    /// <summary>Published, ongoing and finished competitions, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CompetitionResponse>), 200)]
    public async Task<IActionResult> GetList([FromQuery] CompetitionListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetCompetitionsAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CompetitionResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetCompetitionAsync(id, cancellationToken));

    /// <summary>The leaderboard of a competition, ordered by rank then score.</summary>
    [HttpGet("{id:int}/leaderboard")]
    [ProducesResponseType(typeof(IReadOnlyList<CompetitionLeaderboardEntry>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetLeaderboard(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetLeaderboardAsync(id, cancellationToken));

    // ── UC-57 Competition Registration ────────────────────────────

    /// <summary>Register for a published competition inside its registration window.</summary>
    [HttpPost("{id:int}/register")]
    [Authorize(Roles = MemberRoles)]
    [ProducesResponseType(typeof(CompetitionParticipantResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Register(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.RegisterAsync(id, userId, ClientIp, cancellationToken));

    // ── UC-58 View Competition History ────────────────────────────

    /// <summary>The signed-in member's own registrations and results.</summary>
    [HttpGet("my/history")]
    [Authorize(Roles = MemberRoles)]
    [ProducesResponseType(typeof(PagedResponse<CompetitionParticipantResponse>), 200)]
    public Task<IActionResult> GetMyHistory([FromQuery] CompetitionParticipantListRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.GetMyHistoryAsync(request, userId, cancellationToken));

    // ── UC-59 Manage Competitions ─────────────────────────────────

    /// <summary>Create a competition. It starts as "Draft" and must be published to accept registrations.</summary>
    [HttpPost]
    [Authorize(Roles = ManagerRoles)]
    [ProducesResponseType(typeof(CompetitionResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Create([FromBody] CompetitionRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.CreateAsync(request, userId, ClientIp, cancellationToken));

    [HttpPut("{id:int}")]
    [Authorize(Roles = ManagerRoles)]
    [ProducesResponseType(typeof(CompetitionResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Update(int id, [FromBody] CompetitionRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.UpdateAsync(id, request, userId, ClientIp, cancellationToken));

    /// <summary>Publish a Draft competition so members can register.</summary>
    [HttpPost("{id:int}/publish")]
    [Authorize(Roles = ManagerRoles)]
    [ProducesResponseType(typeof(CompetitionResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Publish(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.PublishAsync(id, userId, ClientIp, cancellationToken));

    /// <summary>Cancel a competition that has not started yet.</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = ManagerRoles)]
    [ProducesResponseType(typeof(CompetitionResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.CancelAsync(id, userId, ClientIp, cancellationToken));

    // ── UC-60 Manage Competition Participants ─────────────────────

    /// <summary>All participants, newest registration first. Filter by competition, status or member name.</summary>
    [HttpGet("participants")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType(typeof(PagedResponse<CompetitionParticipantResponse>), 200)]
    public async Task<IActionResult> GetParticipants([FromQuery] CompetitionParticipantListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetParticipantsAsync(request, cancellationToken));

    /// <summary>Disqualify a participant so it no longer counts towards the capacity.</summary>
    [HttpPost("participants/{joinId:int}/disqualify")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType(typeof(CompetitionParticipantResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Disqualify(int joinId, CancellationToken cancellationToken) =>
        Run(userId => _service.DisqualifyAsync(joinId, userId, ClientIp, cancellationToken));

    /// <summary>Reinstate a disqualified participant.</summary>
    [HttpPost("participants/{joinId:int}/reinstate")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType(typeof(CompetitionParticipantResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Reinstate(int joinId, CancellationToken cancellationToken) =>
        Run(userId => _service.ReinstateAsync(joinId, userId, ClientIp, cancellationToken));

    // ── UC-61 Manage Competition Results ──────────────────────────

    /// <summary>Record the preliminary score and rank of a participant.</summary>
    [HttpPost("participants/{joinId:int}/result")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType(typeof(CompetitionParticipantResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> RecordResult(int joinId, [FromBody] RecordCompetitionResultRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.RecordResultAsync(joinId, request, userId, ClientIp, cancellationToken));

    // ── UC-62 Review and Confirm Competition Results ──────────────

    /// <summary>Confirm the final result of one participant and optionally grant the prize voucher.</summary>
    [HttpPost("results/confirm")]
    [Authorize(Roles = ManagerRoles)]
    [ProducesResponseType(typeof(CompetitionParticipantResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> ConfirmResult([FromBody] ConfirmCompetitionResultRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.ConfirmResultAsync(request, userId, ClientIp, cancellationToken));

    /// <summary>Confirm every participant that already has a rank.</summary>
    [HttpPost("{id:int}/results/confirm-all")]
    [Authorize(Roles = ManagerRoles)]
    [ProducesResponseType(typeof(int), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> ConfirmAllResults(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.ConfirmAllResultsAsync(id, userId, ClientIp, cancellationToken));

    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await action(userId));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
