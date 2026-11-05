using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Supplements;

namespace VFMS.API.Controllers;

/// <summary>
/// UC-77 View Supplement Products (Customer, Member) and UC-83 Manage Supplement Products (Manager).
/// The store only shows Active and OutOfStock products; the Manager sees every status.
/// </summary>
[ApiController]
[Route("api/supplement-products")]
[Authorize]
[Produces("application/json")]
public sealed class SupplementProductsController : ControllerBase
{
    private const string StoreRoles = "Customer,Member";
    private const string ViewRoles = "Customer,Member,Manager";

    private readonly ISupplementProductService _service;

    public SupplementProductsController(ISupplementProductService service)
    {
        _service = service;
    }

    /// <summary>Store list (Active and OutOfStock), filtered by keyword and category.</summary>
    [HttpGet]
    [Authorize(Roles = StoreRoles)]
    [ProducesResponseType(typeof(PagedResponse<SupplementProductResponse>), 200)]
    public async Task<IActionResult> GetStore([FromQuery] SupplementProductListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetStoreProductsAsync(request, cancellationToken));

    /// <summary>Categories in use, for the category filter.</summary>
    [HttpGet("categories")]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), 200)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetCategoriesAsync(!IsManager, cancellationToken));

    /// <summary>Manager list with every status.</summary>
    [HttpGet("manage")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(PagedResponse<SupplementProductResponse>), 200)]
    public async Task<IActionResult> GetAll([FromQuery] SupplementProductListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetAllProductsAsync(request, cancellationToken));

    /// <summary>Product details. Inactive products are not found in the store.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType(typeof(SupplementProductResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetProductAsync(id, storeView: !IsManager, cancellationToken));

    [HttpPost]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SupplementProductResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Create([FromBody] SupplementProductRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.CreateAsync(request, userId, ClientIp, cancellationToken));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SupplementProductResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Update(int id, [FromBody] SupplementProductRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.UpdateAsync(id, request, userId, ClientIp, cancellationToken));

    /// <summary>Set Active, Inactive (hidden from the store) or OutOfStock.</summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(typeof(SupplementProductResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> ChangeStatus(int id, [FromBody] SupplementProductStatusRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.ChangeStatusAsync(id, request.Status, userId, ClientIp, cancellationToken));

    private bool IsManager => User.IsInRole("Manager");

    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await action(userId));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
