using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single scheduled or completed one-on-one PT session.
/// The assigned PT records attendance after the session ends (GBR-103).
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
    /// <summary>The PT who wrote the coaching note (UC 4.6 step 9).</summary>
    public string? NoteAuthorUserId { get; set; }
    public int? ReplacementForSessionId { get; set; }
    public string? TrainerAbsenceReason { get; set; }
    public DateTime? TrainerAbsenceReportedAt { get; set; }
    public PTSessionStatus? StatusBeforeTrainerAbsenceReport { get; set; }
    public DateTime? TrainerAbsenceReviewedAt { get; set; }
    public string? TrainerAbsenceReviewNote { get; set; }
    /// <summary>The Manager who decided the attendance dispute (UC 4.11 step 5).</summary>
    public string? TrainerAbsenceReviewedByUserId { get; set; }
    public bool? TrainerAbsenceApproved { get; set; }
    /// <summary>No longer written: the 20:00 attendance reminder was removed from GBR-103. Kept because columns are never dropped.</summary>
    public DateTime? AttendanceReminderSentAt { get; set; }
    /// <summary>When the PT last recorded attendance (UC 4.6 step 6); null when the PT has not recorded it.</summary>
    public DateTime? AttendanceRecordedAt { get; set; }
    /// <summary>What the PT recorded: true = 'Present', false = 'Absent' (shown on UC 4.11).</summary>
    public bool? AttendanceRecordedPresent { get; set; }
    /// <summary>Historical kiosk-generated attendance timestamp. New visits do not set this field.</summary>
    public DateTime? CheckInCompletedAt { get; set; }
    /// <summary>The freeze whose post-freeze schedule created this session, so withdrawing it restores only its own moves (UC 4.8).</summary>
    public int? PTFreezeId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PTPurchase PTPurchase { get; set; } = null!;
    public PTAvailability? PTAvailability { get; set; }
    public ApplicationUser Member { get; set; } = null!;
    public ApplicationUser PTUser { get; set; } = null!;
}

