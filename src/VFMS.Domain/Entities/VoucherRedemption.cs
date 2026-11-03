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
    public DateTime RedeemedAt { get; set; }
    public VoucherRedemptionStatus RedemptionStatus { get; set; } = VoucherRedemptionStatus.Reserved;

    // ── Navigation Properties ──────────────────────────────────
    public Voucher Voucher { get; set; } = null!;
    public Order Order { get; set; } = null!;
}

