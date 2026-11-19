using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Consultations;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/consultations")]
[Authorize]
[Produces("application/json")]
public sealed class ConsultationsController : ControllerBase
{
    private readonly IConsultationService _service;

    public ConsultationsController(IConsultationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ConsultationListRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        var isSupportAgent = User.IsInRole("Manager") || User.IsInRole("Staff");
        return this.ToActionResult(await _service.ListAsync(
            request, isSupportAgent ? null : userId, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        var isSupportAgent = User.IsInRole("Manager") || User.IsInRole("Staff");
        return this.ToActionResult(await _service.GetAsync(
            id, userId, isSupportAgent, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = "Customer,Member")]
    public async Task<IActionResult> Create([FromBody] ConsultationCreateRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.CreateAsync(request, userId, cancellationToken));
    }

    [HttpPost("{id:int}/messages")]
    public async Task<IActionResult> Send(int id, [FromBody] ConsultationSendRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        var isSupportAgent = User.IsInRole("Manager") || User.IsInRole("Staff");
        return this.ToActionResult(await _service.SendAsync(
            id, request, userId, isSupportAgent, cancellationToken));
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = "Manager,Staff")]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.CloseAsync(id, userId, cancellationToken));
    }
}
