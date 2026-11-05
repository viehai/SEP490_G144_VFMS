using System.Security.Cryptography;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Supplements;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-78 Purchase, UC-79 Cancel, UC-80 Track, UC-81 History (Customer, Member) and UC-82 Manage Supplement Orders (Staff, Manager).
/// GBR-57 flow: Pending → Processing → ReadyForPickup (Gym Pickup) / OutForDelivery (Home Delivery) → Completed;
/// Cancelled only while Pending (GBR-60). GBR-58/59: no online payment, payment is confirmed when completing.
/// GBR-61/70: only Active products can be ordered.
/// </summary>
public sealed class SupplementOrderService : ISupplementOrderService
{
    private const string Entity = nameof(SupplementOrder);
    private const string StaffRole = "Staff";
    private static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    private static readonly SupplementOrderStatus[] OngoingStatuses =
    [
        SupplementOrderStatus.Pending, SupplementOrderStatus.Processing,
        SupplementOrderStatus.ReadyForPickup, SupplementOrderStatus.OutForDelivery
    ];

    private readonly ISupplementRepository _repository;
    private readonly IMapper _mapper;

    public SupplementOrderService(ISupplementRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    // ── Customer / Member ────────────────────────────────────────

    public async Task<Result<SupplementOrderResponse>> PlaceOrderAsync(
        PlaceSupplementOrderRequest request, string customerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!EquipmentService.TryParse(request.FulfillmentType, out SupplementDeliveryType deliveryType))
            return Result<SupplementOrderResponse>.Failure(
                "Fulfillment type must be GymPickup or HomeDelivery.", "SUPPLEMENT_ORDER_INVALID_FULFILLMENT");
        var homeDelivery = deliveryType == SupplementDeliveryType.HomeDelivery;
        if (homeDelivery && (EquipmentService.Clean(request.RecipientName) is null || EquipmentService.Clean(request.DeliveryAddress) is null))
            return Result<SupplementOrderResponse>.Failure(
                "Recipient name and delivery address are required for Home Delivery.", "SUPPLEMENT_ORDER_DELIVERY_REQUIRED");

        // The same product added twice is one line with the combined quantity.
        var lines = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .ToArray();
        if (lines.Any(l => l.Quantity > 999))
            return Result<SupplementOrderResponse>.Failure(
                "Quantity must be between 1 and 999 per product.", "SUPPLEMENT_ORDER_QUANTITY_INVALID");

        var products = (await _repository.GetProductsAsync(lines.Select(l => l.ProductId).ToArray(), cancellationToken))
            .ToDictionary(p => p.Id);
        var unavailable = lines
            .Where(l => !products.TryGetValue(l.ProductId, out var p) || p.ProductStatus != SupplementProductStatus.Active)
            .Select(l => l.ProductId)
            .ToArray();
        if (unavailable.Length > 0)
            return Result<SupplementOrderResponse>.Failure(
                $"These products are no longer available and must be removed from the cart: {string.Join(", ", unavailable)}.",
                "SUPPLEMENT_PRODUCT_UNAVAILABLE", 409);

        var order = new SupplementOrder
        {
            CustomerUserId = customerUserId,
            OrderNumber = await NewOrderNumberAsync(DateTime.UtcNow, cancellationToken),
            OrderStatus = SupplementOrderStatus.Pending,
            DeliveryType = deliveryType,
            RecipientName = homeDelivery ? EquipmentService.Clean(request.RecipientName) : null,
            DeliveryAddress = homeDelivery ? EquipmentService.Clean(request.DeliveryAddress) : null,
            DeliveryNote = homeDelivery ? EquipmentService.Clean(request.DeliveryNote) : null,
            Items = lines.Select(l => new SupplementOrderItem
            {
                SupplementProductId = l.ProductId,
                Quantity = l.Quantity,
                UnitPriceAtPurchase = products[l.ProductId].UnitPrice
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);

        var audit = AuditedWriter.Audit(customerUserId, AuditActionType.Create, Entity, ipAddress,
            newValue: $"Number={order.OrderNumber}; Fulfillment={deliveryType}; Items={order.Items.Count}; Total={order.TotalAmount}");
        var staff = await _repository.GetActiveUserIdsInRoleAsync(StaffRole, cancellationToken);
        var notifications = staff
            .Where(id => id != customerUserId)
            .Select(id => AuditedWriter.Notify(id, customerUserId, "New supplement order",
                $"{order.OrderNumber} ({Describe(deliveryType)}): {order.TotalAmount:N0} VND", Entity, NotificationType.SystemAlert))
            .ToArray();

        await AuditedWriter.SaveNewAsync(_repository, order, audit, notifications, cancellationToken);
        return await Reload(order.Id, 201, cancellationToken);
    }

    public Task<Result<PagedResponse<SupplementOrderResponse>>> GetHistoryAsync(
        SupplementOrderListRequest request, string customerUserId, CancellationToken cancellationToken = default) =>
        SearchAsync(request with { FulfillmentType = null, Search = null }, customerUserId, null, cancellationToken);

    public Task<Result<PagedResponse<SupplementOrderResponse>>> GetOngoingAsync(
        string customerUserId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        SearchAsync(new SupplementOrderListRequest(Page: page, PageSize: pageSize), customerUserId, OngoingStatuses, cancellationToken);

    public async Task<Result<SupplementOrderResponse>> CancelAsync(
        int id, string customerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetOrderAsync(id, cancellationToken);
        if (order is null || order.CustomerUserId != customerUserId)
            return NotFound();
        // GBR-60: only while Pending; covers an order processed after the customer opened it (UC-79 A1).
        if (order.OrderStatus != SupplementOrderStatus.Pending)
            return InvalidState(order, "This order can no longer be cancelled.");

        order.OrderStatus = SupplementOrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        var audit = AuditedWriter.Audit(customerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={SupplementOrderStatus.Pending}", $"Status={order.OrderStatus}");
        await AuditedWriter.SaveChangedAsync(_repository, order, audit, null, cancellationToken);
        return await Reload(order.Id, 200, cancellationToken);
    }

    public async Task<Result<SupplementOrderResponse>> GetAsync(
        int id, string? onlyCustomerUserId, CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetOrderAsync(id, cancellationToken);
        if (order is null || onlyCustomerUserId is not null && order.CustomerUserId != onlyCustomerUserId)
            return NotFound();
        return Result<SupplementOrderResponse>.Success(_mapper.Map<SupplementOrderResponse>(order));
    }

    // ── Staff / Manager (UC-82) ──────────────────────────────────

    public Task<Result<PagedResponse<SupplementOrderResponse>>> GetAllAsync(
        SupplementOrderListRequest request, CancellationToken cancellationToken = default) =>
        SearchAsync(request, null, null, cancellationToken);

    public Task<Result<SupplementOrderResponse>> ProcessAsync(
        int id, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, SupplementOrderStatus.Pending, SupplementOrderStatus.Processing, null,
            "Your supplement order is being prepared", handlerUserId, ipAddress, cancellationToken);

    public Task<Result<SupplementOrderResponse>> ReadyForPickupAsync(
        int id, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, SupplementOrderStatus.Processing, SupplementOrderStatus.ReadyForPickup, SupplementDeliveryType.GymPickup,
            "Your supplement order is ready for pickup", handlerUserId, ipAddress, cancellationToken);

    public Task<Result<SupplementOrderResponse>> OutForDeliveryAsync(
        int id, SupplementOutForDeliveryRequest request, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var courier = EquipmentService.Clean(request.CourierName);
        var tracking = EquipmentService.Clean(request.TrackingCode);
        if (courier is null || tracking is null)
            return Task.FromResult(Result<SupplementOrderResponse>.Failure(
                "Courier name and tracking code are required.", "SUPPLEMENT_ORDER_DELIVERY_INFO_REQUIRED"));

        return TransitionAsync(id, SupplementOrderStatus.Processing, SupplementOrderStatus.OutForDelivery,
            SupplementDeliveryType.HomeDelivery, "Your supplement order is out for delivery", handlerUserId, ipAddress, cancellationToken,
            order =>
            {
                order.Delivery ??= new SupplementOrderDelivery { SupplementOrderId = order.Id };
                order.Delivery.CourierName = courier;
                order.Delivery.TrackingCode = tracking;
                order.Delivery.RecipientName = order.RecipientName;
                order.Delivery.DeliveryAddress = order.DeliveryAddress ?? string.Empty;
                order.Delivery.Note = order.DeliveryNote;
                return $"; Courier={courier}; Tracking={tracking}";
            });
    }

    public async Task<Result<SupplementOrderResponse>> CompleteAsync(
        int id, string handlerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        var expected = order.DeliveryType == SupplementDeliveryType.HomeDelivery
            ? SupplementOrderStatus.OutForDelivery
            : SupplementOrderStatus.ReadyForPickup;
        return await TransitionAsync(order, expected, SupplementOrderStatus.Completed, null,
            "Supplement order completed", handlerUserId, ipAddress, cancellationToken,
            o =>
            {
                var now = DateTime.UtcNow;
                o.CompletedAt = now;
                if (o.Delivery is not null)
                    o.Delivery.DeliveredAt = now;
                return o.DeliveryType == SupplementDeliveryType.HomeDelivery ? "; Payment=COD collected" : "; Payment=Collected at counter";
            });
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<Result<SupplementOrderResponse>> TransitionAsync(
        int id, SupplementOrderStatus from, SupplementOrderStatus to, SupplementDeliveryType? requiredType,
        string customerTitle, string handlerUserId, string? ipAddress, CancellationToken cancellationToken,
        Func<SupplementOrder, string>? apply = null)
    {
        var order = await _repository.GetOrderAsync(id, cancellationToken);
        return order is null
            ? NotFound()
            : await TransitionAsync(order, from, to, requiredType, customerTitle, handlerUserId, ipAddress, cancellationToken, apply);
    }

    private async Task<Result<SupplementOrderResponse>> TransitionAsync(
        SupplementOrder order, SupplementOrderStatus from, SupplementOrderStatus to, SupplementDeliveryType? requiredType,
        string customerTitle, string handlerUserId, string? ipAddress, CancellationToken cancellationToken,
        Func<SupplementOrder, string>? apply)
    {
        // UC-82 AF3: an order cancelled by the customer (even while being processed) allows no action.
        if (order.OrderStatus == SupplementOrderStatus.Cancelled)
            return Result<SupplementOrderResponse>.Failure(
                $"Order {order.OrderNumber} was cancelled by the customer. No action is available.", "SUPPLEMENT_ORDER_CANCELLED", 409);
        if (requiredType is { } type && order.DeliveryType != type)
            return Result<SupplementOrderResponse>.Failure(
                $"This action is only for {Describe(type)} orders. Order {order.OrderNumber} is {Describe(order.DeliveryType)}.",
                "SUPPLEMENT_ORDER_WRONG_FULFILLMENT", 409);
        // UC-82 AF4: the change must follow the order flow.
        if (order.OrderStatus != from)
            return InvalidState(order, $"Only {from} orders can be moved to {to}.");

        var extra = apply?.Invoke(order) ?? string.Empty;
        order.OrderStatus = to;
        var audit = AuditedWriter.Audit(handlerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={from}", $"Status={to}{extra}");
        var message = to switch
        {
            SupplementOrderStatus.OutForDelivery =>
                $"{order.OrderNumber} is on its way with {order.Delivery?.CourierName} (tracking code {order.Delivery?.TrackingCode}).",
            SupplementOrderStatus.ReadyForPickup =>
                $"{order.OrderNumber} is ready. Please pay {order.TotalAmount:N0} VND at the counter to collect it.",
            SupplementOrderStatus.Completed =>
                $"{order.OrderNumber}: payment of {order.TotalAmount:N0} VND received. Thank you!",
            _ => $"{order.OrderNumber} is now {to}."
        };
        var notifications = order.CustomerUserId == handlerUserId
            ? null
            : new[]
            {
                AuditedWriter.Notify(order.CustomerUserId, handlerUserId, customerTitle, message, Entity,
                    to == SupplementOrderStatus.Completed ? NotificationType.PaymentConfirmed : NotificationType.SystemAlert)
            };
        await AuditedWriter.SaveChangedAsync(_repository, order, audit, notifications, cancellationToken);
        return await Reload(order.Id, 200, cancellationToken);
    }

    private async Task<Result<PagedResponse<SupplementOrderResponse>>> SearchAsync(
        SupplementOrderListRequest request, string? customerUserId, IReadOnlyCollection<SupplementOrderStatus>? statuses,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EquipmentService.TryParse(request.Status, out SupplementOrderStatus parsed))
                return Result<PagedResponse<SupplementOrderResponse>>.Failure("Status filter is invalid.", "SUPPLEMENT_ORDER_INVALID_STATUS");
            statuses = statuses is null || statuses.Contains(parsed) ? [parsed] : [];
        }
        SupplementDeliveryType? deliveryType = null;
        if (!string.IsNullOrWhiteSpace(request.FulfillmentType))
        {
            if (!EquipmentService.TryParse(request.FulfillmentType, out SupplementDeliveryType parsed))
                return Result<PagedResponse<SupplementOrderResponse>>.Failure(
                    "Fulfillment type filter is invalid.", "SUPPLEMENT_ORDER_INVALID_FULFILLMENT");
            deliveryType = parsed;
        }
        if (request.From is { } f && request.To is { } t && f > t)
            return Result<PagedResponse<SupplementOrderResponse>>.Failure(
                "The start date cannot be after the end date.", "SUPPLEMENT_ORDER_INVALID_DATE_RANGE");

        // Gym dates (UTC+7) → UTC instants; "To" includes the whole day.
        DateTime? fromUtc = request.From?.ToDateTime(TimeOnly.MinValue) - GymOffset;
        DateTime? toUtcExclusive = request.To?.AddDays(1).ToDateTime(TimeOnly.MinValue) - GymOffset;

        var (items, total) = await _repository.SearchOrdersAsync(
            customerUserId, statuses, deliveryType, fromUtc, toUtcExclusive, EquipmentService.Clean(request.Search),
            request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<SupplementOrderResponse>>.Success(new PagedResponse<SupplementOrderResponse>(
            request.Page, request.PageSize, total, items.Select(_mapper.Map<SupplementOrderResponse>).ToArray()));
    }

    private async Task<string> NewOrderNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = $"SO-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
            if (!await _repository.OrderNumberExistsAsync(number, cancellationToken))
                return number;
        }
        return $"SO-{now:yyyyMMddHHmmssfff}";
    }

    private static string Describe(SupplementDeliveryType type) =>
        type == SupplementDeliveryType.HomeDelivery ? "Home Delivery" : "Gym Pickup";

    private async Task<Result<SupplementOrderResponse>> Reload(int id, int statusCode, CancellationToken cancellationToken)
    {
        var order = await _repository.GetOrderAsync(id, cancellationToken);
        return order is null
            ? NotFound()
            : Result<SupplementOrderResponse>.Success(_mapper.Map<SupplementOrderResponse>(order), statusCode);
    }

    private static Result<SupplementOrderResponse> InvalidState(SupplementOrder order, string message) =>
        Result<SupplementOrderResponse>.Failure(
            $"{message} Current status: {order.OrderStatus}.", "SUPPLEMENT_ORDER_INVALID_STATE", 409);

    private static Result<SupplementOrderResponse> NotFound() =>
        Result<SupplementOrderResponse>.Failure("Supplement order was not found.", "SUPPLEMENT_ORDER_NOT_FOUND", 404);
}
