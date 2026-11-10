using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single scheduled or completed one-on-one PT session.
/// Self check-in within [-30 mins, +15 mins] of a scheduled PT session automatically marks it Completed (GBR-92).
/// </summary>
public class PTSession : BaseEntity
{
    public int PTPurchaseId { get; set; }
    public int? PTAvailabilityId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public string PTUserId { get; set; } = string.Empty;
    public DateOnly SessionDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public PTSessionStatus SessionStatus { get; set; } = PTSessionStatus.Scheduled;
    public string? Note { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int CreditUnits { get; set; } = 1;
    public bool IsCreditForfeited { get; set; }
    public DateOnly? ProposedDate { get; set; }
    public TimeOnly? ProposedStartTime { get; set; }
    public TimeOnly? ProposedEndTime { get; set; }
    public PTAdjustmentType? ProposedAdjustmentType { get; set; }
    public DateTime? ProposalExpiresAt { get; set; }
    public DateTime? ProposalEmailSentAt { get; set; }
    public DateTime? ProposalEmailRetryAt { get; set; }
    public DateTime? NoteUpdatedAt { get; set; }
    public int? ReplacementForSessionId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PTPurchase PTPurchase { get; set; } = null!;
    public PTAvailability? PTAvailability { get; set; }
    public ApplicationUser Member { get; set; } = null!;
    public ApplicationUser PTUser { get; set; } = null!;
}

