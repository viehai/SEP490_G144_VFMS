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

/// <summary>UC-48/UC-57 View Active Check-ins (Staff, Manager).</summary>
[ApiController]
[Route("api/check-ins")]
[Authorize(Roles = "Staff,Manager")]
[Produces("application/json")]
public sealed class CheckInsController : ControllerBase
{
    private readonly IActiveCheckInService _service;

    public CheckInsController(IActiveCheckInService service) => _service = service;

    [HttpGet("active")]
    public async Task<IActionResult> GetActive([FromQuery] string? search, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetActiveAsync(search, cancellationToken));
}
