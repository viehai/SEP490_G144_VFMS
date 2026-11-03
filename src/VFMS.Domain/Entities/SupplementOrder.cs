using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Supplement order header.
/// Status flow: Pending → Processing → ReadyForPickup / OutForDelivery → Completed.
/// Customers/Members can cancel only while <c>Pending</c> (GBR-57).
/// Offline payment only (GymPickup or HomeDelivery COD).
/// </summary>
public class SupplementOrder : BaseEntity
{
    public string CustomerUserId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public SupplementOrderStatus OrderStatus { get; set; } = SupplementOrderStatus.Pending;
    public SupplementDeliveryType DeliveryType { get; set; }
    public string? RecipientName { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? DeliveryNote { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Customer { get; set; } = null!;
    public ICollection<SupplementOrderItem> Items { get; set; } = [];
    public SupplementOrderDelivery? Delivery { get; set; }
}

