namespace VFMS.Contracts.Supplements;

// ── Products (UC-77 View, UC-83 Manage) ──────────────────────────

/// <summary>
/// Create or edit a supplement product (UC-83). Status: Active (default), Inactive or OutOfStock.
/// The image is a link to a png, jpg or jpeg file.
/// </summary>
public sealed record SupplementProductRequest(
    string Name,
    string Description,
    string ImageUrl,
    decimal UnitPrice,
    string? Category = null,
    string Status = "Active"
);

public sealed record SupplementProductStatusRequest(string Status);

/// <summary>
/// Product search. The store (Customer, Member) only ever sees Active and OutOfStock products;
/// the Manager can also filter by any status.
/// </summary>
public sealed record SupplementProductListRequest(
    string? Search = null,
    string? Category = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record SupplementProductResponse(
    int Id,
    string Name,
    string? Category,
    string? Description,
    string? ImageUrl,
    decimal UnitPrice,
    string Status,
    bool CanOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// ── Orders (UC-78..82) ───────────────────────────────────────────

public sealed record SupplementOrderLineRequest(int ProductId, int Quantity);

/// <summary>
/// Place an order (UC-78). FulfillmentType: GymPickup (paid at the counter) or HomeDelivery (Cash on Delivery).
/// Recipient name and delivery address are required for HomeDelivery and ignored for GymPickup.
/// </summary>
public sealed record PlaceSupplementOrderRequest(
    IReadOnlyList<SupplementOrderLineRequest> Items,
    string FulfillmentType,
    string? RecipientName = null,
    string? DeliveryAddress = null,
    string? DeliveryNote = null
);

/// <summary>
/// Order filters. Dates are gym dates (UTC+7) and both ends are inclusive.
/// FulfillmentType and Search (order number or customer name) are used by Staff and Manager only.
/// </summary>
public sealed record SupplementOrderListRequest(
    string? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? FulfillmentType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record SupplementOutForDeliveryRequest(string CourierName, string TrackingCode);

public sealed record SupplementOrderItemResponse(
    int Id,
    int ProductId,
    string? ProductName,
    string? ImageUrl,
    int Quantity,
    decimal UnitPriceAtPurchase,
    decimal LineTotal
);

public sealed record SupplementOrderDeliveryResponse(
    string? RecipientName,
    string? DeliveryAddress,
    string? DeliveryNote,
    string? ContactPhone,
    string? CourierName,
    string? TrackingCode,
    DateTime? DeliveredAt
);

public sealed record SupplementOrderResponse(
    int Id,
    string OrderNumber,
    string Status,
    string FulfillmentType,
    decimal TotalAmount,
    DateTime PlacedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string CustomerUserId,
    string? CustomerName,
    bool CanCancel,
    IReadOnlyCollection<SupplementOrderItemResponse> Items,
    SupplementOrderDeliveryResponse? Delivery
);
