using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Gym check-in/check-out record.
/// Enforces 15-minute anti-passback cooldown (GBR-90).
/// Auto-closed by JOB-04 at gym closing time + 30 min or after 6 hours.
/// </summary>
public class CheckIn : BaseEntity
{
    public string MemberUserId { get; set; } = string.Empty;
    public int? MemberMembershipId { get; set; }
    public DateTime CheckinTime { get; set; }
    public DateTime? CheckoutTime { get; set; }
    public CheckInMethod CheckinMethod { get; set; }
    public CheckInMethod? CheckoutMethod { get; set; }
    /// <summary>Populated when Staff performs manual check-in/check-out (UC-55, UC-56).</summary>
    public string? HandledByUserId { get; set; }
    public CheckInStatus Status { get; set; } = CheckInStatus.Active;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Member { get; set; } = null!;
    public MemberMembership? MemberMembership { get; set; }
    public ApplicationUser? HandledByUser { get; set; }
}

