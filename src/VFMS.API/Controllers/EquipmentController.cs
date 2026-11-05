using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;

namespace VFMS.API.Controllers;

/// <summary>UC-76 Manage Gym Equipment. Any signed-in user can read; only the Manager can change.</summary>
[ApiController]
[Route("api/equipment")]
[Authorize]
[Produces("application/json")]
public sealed class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _service;

    public EquipmentController(IEquipmentService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<EquipmentResponse>), 200)]
    public async Task<IActionResult> GetList([FromQuery] EquipmentListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetEquipmentListAsync(request, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EquipmentResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetEquipmentAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(EquipmentResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] EquipmentRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.CreateEquipmentAsync(request, userId, ClientIp, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(EquipmentResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Update(int id, [FromBody] EquipmentRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.UpdateEquipmentAsync(id, request, userId, ClientIp, cancellationToken));
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(EquipmentResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] EquipmentStatusRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.ChangeStatusAsync(id, request, userId, ClientIp, cancellationToken));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Equipment categories (part of UC-76).</summary>
[ApiController]
[Route("api/equipment-categories")]
[Authorize]
[Produces("application/json")]
public sealed class EquipmentCategoriesController : ControllerBase
{
    private readonly IEquipmentService _service;

    public EquipmentCategoriesController(IEquipmentService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EquipmentCategoryResponse>), 200)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetCategoriesAsync(cancellationToken));

    [HttpPost]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(EquipmentCategoryResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] EquipmentCategoryRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.CreateCategoryAsync(request, userId, ClientIp, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(EquipmentCategoryResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Update(int id, [FromBody] EquipmentCategoryRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await _service.UpdateCategoryAsync(id, request, userId, ClientIp, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        var result = await _service.DeleteCategoryAsync(id, userId, ClientIp, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result);
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
