using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Prize definition for a <see cref="Competition"/> (configured by Manager).
/// May include a voucher reward.
/// </summary>
public class CompetitionPrize : BaseEntity
{
    public int CompetitionId { get; set; }
    public string PrizeName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string? Description { get; set; }
    /// <summary>Optional voucher reward linked to this prize.</summary>
    public int? VoucherId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Competition Competition { get; set; } = null!;
    public Voucher? Voucher { get; set; }
}

