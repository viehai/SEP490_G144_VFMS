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
    public FreezeRequestStatus RequestStatus { get; set; } = FreezeRequestStatus.Pending;
    public string? ReviewNote { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? EarlyUnfreezeAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public MemberMembership MemberMembership { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
}

