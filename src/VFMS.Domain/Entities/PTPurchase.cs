using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A PT package purchase contract between a member and a personal trainer.
/// Requires an active General Gym Membership covering the training period (GBR-10).
/// </summary>
public class PTPurchase : BaseEntity
{
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public PTPurchaseStatus Status { get; set; } = PTPurchaseStatus.Active;
    public string? PackageNameSnapshot { get; set; }
    public TrainerLevel? TrainerLevelSnapshot { get; set; }
    public int FreezeMinimumDaysSnapshot { get; set; }
    public int FreezeMaxTotalDaysSnapshot { get; set; }
    public string? WeeklyPatternJson { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? ReceiptEmailSentAt { get; set; }
    public DateTime? ReceiptEmailRetryAt { get; set; }
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

