using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Order header – represents a checkout transaction.
/// Customers and Members can purchase multiple packages in a single order (GBR-36).
/// </summary>
public class Order : BaseEntity
{
    public string PurchaserUserId { get; set; } = string.Empty;
    /// <summary>Human-readable unique order reference (e.g. ORD-20240101-0001). Used in VietQR payload.</summary>
    public string OrderNumber { get; set; } = string.Empty;
    public OrderChannel Channel { get; set; }
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;
    public DateTime? CompletedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Purchaser { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = [];
    public PaymentTransaction? PaymentTransaction { get; set; }
    public VoucherRedemption? VoucherRedemption { get; set; }
}

