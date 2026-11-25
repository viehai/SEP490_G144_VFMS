using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A purchased service entitlement provisioned from an <see cref="OrderItem"/>.
/// Linked 1-to-1 with a <see cref="PackageComponent"/> and tracks lifecycle status.
/// </summary>
public class MemberMembership : BaseEntity
{
    public int OrderItemId { get; set; }
    public int PackageComponentId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Scheduled;
    // AwaitingActivation has no service term until the customer or the commerce worker assigns it.
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>Remaining PT sessions (null for GYM/YOGA components).</summary>
    public int? RemainingSessionCount { get; set; }
    /// <summary>Gym freeze visits granted at settlement. Null identifies a legacy cumulative-day entitlement.</summary>
    public int? GrantedFreezeCount { get; set; }
    public int? GrantedMinFreezeDays { get; set; }
    public int? GrantedMaxFreezeDays { get; set; }
    public int? GrantedFreezeAllowanceDays { get; set; }
    /// <summary>Total days currently frozen (cumulative).</summary>
    public int TotalFrozenDays { get; set; } = 0;
    /// <summary>Total number of freeze requests used.</summary>
    public int FreezeCount { get; set; } = 0;

    // ── Navigation Properties ──────────────────────────────────
    public OrderItem OrderItem { get; set; } = null!;
    public PackageComponent PackageComponent { get; set; } = null!;
    public ApplicationUser Member { get; set; } = null!;
    public ICollection<MembershipFreeze> MembershipFreezes { get; set; } = [];
}

