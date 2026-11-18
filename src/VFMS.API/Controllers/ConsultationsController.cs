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
        return this.ToActionResult(await _service.ListAsync(
            request, User.IsInRole("Manager") ? null : userId, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.GetAsync(
            id, userId, User.IsInRole("Manager"), cancellationToken));
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
        return this.ToActionResult(await _service.SendAsync(
            id, request, userId, User.IsInRole("Manager"), cancellationToken));
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Unauthorized();
        return this.ToActionResult(await _service.CloseAsync(id, userId, cancellationToken));
    }
}
