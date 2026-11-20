namespace VFMS.Contracts.Supplements;

// ── Products (UC-77 View, UC-83 Manage) ──────────────────────────

/// <summary>
/// Create or edit a supplement product (UC-83). Status: Active (default), Inactive or OutOfStock.
/// The image is a link to a png, jpg or jpeg file. Brand, Size (pack size, e.g. "5 lbs") and Flavour are optional.
/// StockQuantity is the starting stock when creating (blank = stock not tracked); an edit ignores it, stock is
/// changed with the stock action instead so orders placed meanwhile are never overwritten.
/// </summary>
public sealed record SupplementProductRequest(
    string Name,
    string Description,
    string ImageUrl,
    decimal UnitPrice,
    string? Category = null,
    string Status = "Active",
    string? Brand = null,
    string? Size = null,
    string? Flavour = null,
    int? StockQuantity = null
);

/// <summary>Stock rules shared by the API and the screens.</summary>
public static class SupplementStock
{
    /// <summary>At or below this many units a tracked product is "low on stock".</summary>
    public const int LowStockThreshold = 5;
    public const int MaxQuantity = 100_000;

    /// <summary>Add Quantity units (a delivery arrived); starts tracking at 0 when stock was not tracked.</summary>
    public const string Add = "Add";
    /// <summary>Set the stock to exactly Quantity (after a stock count).</summary>
    public const string Set = "Set";
    /// <summary>Stop tracking stock for the product; Quantity is ignored.</summary>
    public const string StopTracking = "StopTracking";

    public static bool IsLow(int? stock) => stock is { } units && units <= LowStockThreshold;
}

/// <summary>Change a product's stock (Manager). Action: Add, Set or StopTracking. Note is kept in the audit log.</summary>
public sealed record SupplementStockRequest(string Action, int Quantity = 0, string? Note = null);

public sealed record SupplementProductStatusRequest(string Status);

/// <summary>
/// Product search. The store (Customer, Member) only ever sees Active and OutOfStock products;
/// the Manager can also filter by any status.
/// Brand is an exact match; MinPrice and MaxPrice are inclusive, in VND.
/// Sort is one of name (default), price_asc, price_desc or newest.
/// LowStock (Manager only) keeps tracked products with at most SupplementStock.LowStockThreshold units.
/// In the store, StockQuantity is only filled in when the product is low on stock.
/// </summary>
public sealed record SupplementProductListRequest(
    string? Search = null,
    string? Category = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20,
    string? Brand = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string? Sort = null,
    bool LowStock = false
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
    DateTime UpdatedAt,
    string? Brand = null,
    string? Size = null,
    string? Flavour = null,
    int? StockQuantity = null,
    bool LowStock = false
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
