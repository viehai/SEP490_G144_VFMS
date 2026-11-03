using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Voucher / promotional code configured by a Manager.
/// Only one voucher can be applied per order (GBR-45, no stacking).
/// </summary>
public class Voucher : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public int? MaxRedemptions { get; set; }
    public int? MaxRedemptionsPerUser { get; set; }
    public decimal? MinimumSpend { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public VoucherStatus VoucherStatus { get; set; } = VoucherStatus.Active;
    public string CreatedByUserId { get; set; } = string.Empty;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<VoucherRedemption> Redemptions { get; set; } = [];
}

