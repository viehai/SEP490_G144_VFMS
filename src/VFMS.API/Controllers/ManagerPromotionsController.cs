using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Enums;

namespace VFMS.API.Controllers;

[ApiController]
[Route("api/manager/promotions")]
[Authorize(Roles = nameof(UserRole.Manager))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerPromotionsController(IPromotionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PromotionResponse>), 200)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        User.GetUserId() is not null ? this.ToActionResult(await service.ListAsync(ct)) : Unauthorized();

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PromotionResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        User.GetUserId() is not null ? this.ToActionResult(await service.GetAsync(id, ct)) : Unauthorized();

    [HttpPost]
    [ProducesResponseType(typeof(PromotionResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] CreatePromotionRequest request, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.CreateAsync(request, userId, IpAddress, ct)) : Unauthorized();

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(PromotionResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePromotionRequest request, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.UpdateAsync(id, request, userId, IpAddress, ct)) : Unauthorized();

    [HttpPatch("{id:int}/enabled")]
    [ProducesResponseType(typeof(PromotionResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetEnabled(int id, [FromBody] SetPromotionEnabledRequest request, CancellationToken ct) =>
        User.GetUserId() is { } userId
            ? this.ToActionResult(await service.SetEnabledAsync(id, request, userId, IpAddress, ct)) : Unauthorized();

    private string? IpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();
}
