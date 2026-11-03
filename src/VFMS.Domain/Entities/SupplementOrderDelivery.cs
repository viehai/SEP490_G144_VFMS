using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Delivery tracking record for a <see cref="SupplementOrder"/> (HomeDelivery COD).
/// </summary>
public class SupplementOrderDelivery : BaseEntity
{
    public int SupplementOrderId { get; set; }
    public string? TrackingCode { get; set; }
    public string? CourierName { get; set; }
    public string? RecipientName { get; set; }
    public string DeliveryAddress { get; set; } = string.Empty;
    public DateTime? DeliveredAt { get; set; }
    public string? Note { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public SupplementOrder SupplementOrder { get; set; } = null!;
}

