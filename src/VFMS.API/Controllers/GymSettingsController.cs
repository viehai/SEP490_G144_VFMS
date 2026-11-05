using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.SystemAdministration;

namespace VFMS.API.Controllers;

/// <summary>UC-84 Manage General Policies: the gym's name, contact details, opening hours and new-member intake.</summary>
[ApiController]
[Route("api/gym-settings")]
[Authorize]
[Produces("application/json")]
public sealed class GymSettingsController : ControllerBase
{
    private readonly IGymSettingsService _service;

    public GymSettingsController(IGymSettingsService service)
    {
        _service = service;
    }

    /// <summary>Current settings. Readable by any signed-in user, since other functions show them.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GymSettingsResponse), 200)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetAsync(cancellationToken));

    [HttpPut]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(GymSettingsResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Update([FromBody] GymSettingsRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.UpdateAsync(
            request, userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));
    }
}
