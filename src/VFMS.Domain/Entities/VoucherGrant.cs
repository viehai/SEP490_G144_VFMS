using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>One use of an assigned voucher awarded to a confirmed competition winner.</summary>
public sealed class VoucherGrant : BaseEntity
{
    public int CompetitionPrizeId { get; set; }
    public int VoucherId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public CompetitionPrize CompetitionPrize { get; set; } = null!;
    public Voucher Voucher { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
