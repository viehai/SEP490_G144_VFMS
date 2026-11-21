using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Equipment;

namespace VFMS.API.Controllers;

/// <summary>UC-70 View Gym Details &amp; Facilities: public, so guests can see the gym before they sign up.</summary>
[ApiController]
[Route("api/gym-details")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class GymDetailsController : ControllerBase
{
    private readonly IGymDetailsService _service;

    public GymDetailsController(IGymDetailsService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GymDetailsResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetAsync(cancellationToken));
}
