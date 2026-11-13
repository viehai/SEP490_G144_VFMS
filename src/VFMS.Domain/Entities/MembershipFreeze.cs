using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A member's gym membership freeze request.
/// Upon Manager approval, extends <see cref="MemberMembership.EndDate"/> by frozen days (GBR-43).
/// Weekly Yoga packages cannot be frozen (GBR-85).
/// </summary>
public class MembershipFreeze : BaseEntity
{
    public int MemberMembershipId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateOnly RequestedStartDate { get; set; }
    public DateOnly RequestedEndDate { get; set; }
    public DateOnly? ActualStartDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }
    public string? Reason { get; set; }
    public int? FreezePolicyId { get; set; }
    /// <summary>UC-16 policy values captured on submission or a Member edit.</summary>
    public string? AppliedPolicySnapshot { get; set; }
    public FreezeRequestStatus RequestStatus { get; set; } = FreezeRequestStatus.Pending;
    public string? ReviewNote { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? EarlyUnfreezeAt { get; set; }
    /// <summary>Member submission for later UC-17 review, separate from execution.</summary>
    public DateTime? EarlyUnfreezeRequestedAt { get; set; }
    public FreezeRequestStatus? EarlyUnfreezeRequestStatus { get; set; }
    /// <summary>Net expiry days already applied by UC-17. JOB-03 must not apply these days again.</summary>
    public int? AppliedExpiryExtensionDays { get; set; }
    public string? EarlyUnfreezeReviewedByUserId { get; set; }
    public DateTime? EarlyUnfreezeReviewedAt { get; set; }
    public string? EarlyUnfreezeReviewNote { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public MemberMembership MemberMembership { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
    public FreezePolicy? FreezePolicy { get; set; }
    public ApplicationUser? EarlyUnfreezeReviewedByUser { get; set; }
}

