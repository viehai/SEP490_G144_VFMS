using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;

namespace VFMS.API.Controllers;

/// <summary>UC-85 Manage Banned Words (Admin).</summary>
[ApiController]
[Route("api/admin/banned-words")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class BannedWordsController : ControllerBase
{
    private readonly IBannedWordService _service;

    public BannedWordsController(IBannedWordService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BannedWordResponse>), 200)]
    public async Task<IActionResult> GetWords([FromQuery] BannedWordListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetWordsAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BannedWordResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetWord(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetWordAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(BannedWordResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] BannedWordRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } actorUserId)
            return Unauthorized();
        return this.ToActionResult(await _service.CreateAsync(request, actorUserId, ClientIp, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(BannedWordResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Update(int id, [FromBody] BannedWordRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } actorUserId)
            return Unauthorized();
        return this.ToActionResult(await _service.UpdateAsync(id, request, actorUserId, ClientIp, cancellationToken));
    }

    [HttpPatch("{id:int}/activate")]
    [ProducesResponseType(typeof(BannedWordResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Activate(int id, CancellationToken cancellationToken) =>
        SetActive(id, true, cancellationToken);

    [HttpPatch("{id:int}/deactivate")]
    [ProducesResponseType(typeof(BannedWordResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        SetActive(id, false, cancellationToken);

    private async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } actorUserId)
            return Unauthorized();
        return this.ToActionResult(await _service.SetActiveAsync(id, isActive, actorUserId, ClientIp, cancellationToken));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
