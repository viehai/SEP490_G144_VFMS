using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Records one voucher application to an order.
/// Released if the order expires (JOB-01) or is cancelled before fulfilment (GBR-49).
/// </summary>
public class VoucherRedemption : BaseEntity
{
    public int VoucherId { get; set; }
    public int OrderId { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>Voucher code captured when applied (for example, SAVE10), independent of later campaign edits.</summary>
    public string? VoucherCodeSnapshot { get; set; }
    /// <summary>Campaign display name captured when applied; this is distinct from its voucher code.</summary>
    public string? VoucherNameSnapshot { get; set; }
    public DateTime RedeemedAt { get; set; }
    public VoucherRedemptionStatus RedemptionStatus { get; set; } = VoucherRedemptionStatus.Reserved;

    // ── Navigation Properties ──────────────────────────────────
    public Voucher Voucher { get; set; } = null!;
    public Order Order { get; set; } = null!;
}

