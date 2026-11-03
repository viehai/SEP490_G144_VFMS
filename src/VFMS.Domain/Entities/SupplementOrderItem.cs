using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single product line within a <see cref="SupplementOrder"/>.
/// </summary>
public class SupplementOrderItem : BaseEntity
{
    public int SupplementOrderId { get; set; }
    public int SupplementProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceAtPurchase { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public SupplementOrder SupplementOrder { get; set; } = null!;
    public SupplementProduct SupplementProduct { get; set; } = null!;
}

