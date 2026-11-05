using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Supplements;

namespace VFMS.API.Controllers;

/// <summary>
/// UC-78 Purchase, UC-79 Cancel, UC-80 Track, UC-81 History (Customer, Member)
/// and UC-82 Manage Supplement Orders (Staff, Manager).
/// </summary>
[ApiController]
[Route("api/supplement-orders")]
[Authorize]
[Produces("application/json")]
public sealed class SupplementOrdersController : ControllerBase
{
    private const string CustomerRoles = "Customer,Member";
    private const string HandlerRoles = "Staff,Manager";

    private readonly ISupplementOrderService _service;

    public SupplementOrdersController(ISupplementOrderService service)
    {
        _service = service;
    }

    // ── Customer / Member ────────────────────────────────────────

    /// <summary>Place an order from the cart. No online payment: Gym Pickup is paid at the counter, Home Delivery by COD.</summary>
    [HttpPost]
    [Authorize(Roles = CustomerRoles)]
    [ProducesResponseType(typeof(SupplementOrderResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Place([FromBody] PlaceSupplementOrderRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.PlaceOrderAsync(request, userId, ClientIp, cancellationToken));

    /// <summary>Purchase history (all of the user's orders), newest first, filtered by status and gym date range.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = CustomerRoles)]
    [ProducesResponseType(typeof(PagedResponse<SupplementOrderResponse>), 200)]
    public Task<IActionResult> GetHistory([FromQuery] SupplementOrderListRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.GetHistoryAsync(request, userId, cancellationToken));

    /// <summary>Orders still in progress (Pending, Processing, ReadyForPickup, OutForDelivery).</summary>
    [HttpGet("mine/ongoing")]
    [Authorize(Roles = CustomerRoles)]
    [ProducesResponseType(typeof(PagedResponse<SupplementOrderResponse>), 200)]
    public Task<IActionResult> GetOngoing([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Page"] = ["Page must be at least 1 and PageSize between 1 and 100."]
            })));
        return Run(userId => _service.GetOngoingAsync(userId, page, pageSize, cancellationToken));
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = CustomerRoles)]
    [ProducesResponseType(typeof(SupplementOrderResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.CancelAsync(id, userId, ClientIp, cancellationToken));

    /// <summary>Customers and Members see only their own orders; Staff and Manager see any order.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Customer,Member,Staff,Manager")]
    [ProducesResponseType(typeof(SupplementOrderResponse), 200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.GetAsync(id, IsHandler ? null : userId, cancellationToken));

    // ── Staff / Manager (UC-82) ──────────────────────────────────

    /// <summary>All orders, newest first. Filter by status, fulfillment type, date range, or search order number / customer name.</summary>
    [HttpGet]
    [Authorize(Roles = HandlerRoles)]
    [ProducesResponseType(typeof(PagedResponse<SupplementOrderResponse>), 200)]
    public async Task<IActionResult> GetAll([FromQuery] SupplementOrderListRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await _service.GetAllAsync(request, cancellationToken));

    /// <summary>Pending → Processing.</summary>
    [HttpPost("{id:int}/process")]
    [Authorize(Roles = HandlerRoles)]
    public Task<IActionResult> Process(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.ProcessAsync(id, userId, ClientIp, cancellationToken));

    /// <summary>Gym Pickup: Processing → ReadyForPickup.</summary>
    [HttpPost("{id:int}/ready-for-pickup")]
    [Authorize(Roles = HandlerRoles)]
    public Task<IActionResult> ReadyForPickup(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.ReadyForPickupAsync(id, userId, ClientIp, cancellationToken));

    /// <summary>Home Delivery: Processing → OutForDelivery with courier name and tracking code.</summary>
    [HttpPost("{id:int}/out-for-delivery")]
    [Authorize(Roles = HandlerRoles)]
    public Task<IActionResult> OutForDelivery(int id, [FromBody] SupplementOutForDeliveryRequest request, CancellationToken cancellationToken) =>
        Run(userId => _service.OutForDeliveryAsync(id, request, userId, ClientIp, cancellationToken));

    /// <summary>Confirm the payment (counter or COD) and complete: ReadyForPickup / OutForDelivery → Completed.</summary>
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = HandlerRoles)]
    public Task<IActionResult> Complete(int id, CancellationToken cancellationToken) =>
        Run(userId => _service.CompleteAsync(id, userId, ClientIp, cancellationToken));

    private bool IsHandler => User.IsInRole("Staff") || User.IsInRole("Manager");

    private async Task<IActionResult> Run<T>(Func<string, Task<Result<T>>> action)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();
        return this.ToActionResult(await action(userId));
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
