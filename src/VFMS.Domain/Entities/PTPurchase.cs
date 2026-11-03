using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// A PT package purchase contract between a member and a personal trainer.
/// Requires an active General Gym Membership covering the training period (GBR-10).
/// </summary>
public class PTPurchase : BaseEntity
{
    public int MemberMembershipId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public string PTUserId { get; set; } = string.Empty;
    public int TotalSessions { get; set; }
    public int RemainingSessions { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public bool IsCompleted { get; set; } = false;

    // ── Navigation Properties ──────────────────────────────────
    public MemberMembership MemberMembership { get; set; } = null!;
    public ApplicationUser Member { get; set; } = null!;
    public ApplicationUser PTUser { get; set; } = null!;
    public ICollection<PTSession> Sessions { get; set; } = [];
    public ICollection<PTFreeze> PTFreezes { get; set; } = [];
}

