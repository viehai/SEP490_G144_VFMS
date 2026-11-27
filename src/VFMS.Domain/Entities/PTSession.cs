using System.ComponentModel.DataAnnotations.Schema;
using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single scheduled or completed one-on-one PT session.
/// Self check-in within [-30 mins, +15 mins] of a scheduled PT session marks it 'Present' (GBR-92, GBR-103).
/// </summary>
public class PTSession : BaseEntity
{
    public int PTPurchaseId { get; set; }
    public int? PTAvailabilityId { get; set; }
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
    [NotMapped]
    public TimeOnly? ProposedEndTime => ProposedStartTime?.Add(EndTime - StartTime);
    public DateTime? ProposalExpiresAt { get; set; }
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
    /// <summary>When the PT last recorded attendance (UC 4.6 step 6); null when the PT has not recorded it.</summary>
    public DateTime? AttendanceRecordedAt { get; set; }
    /// <summary>What the PT recorded: true = 'Present', false = 'Absent' (shown on UC 4.11).</summary>
    public bool? AttendanceRecordedPresent { get; set; }
    /// <summary>The freeze whose post-freeze schedule created this session, so withdrawing it restores only its own moves (UC 4.8).</summary>
    public int? PTFreezeId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PTPurchase PTPurchase { get; set; } = null!;
    public PTAvailability? PTAvailability { get; set; }
    [NotMapped]
    public string MemberUserId => PTPurchase.MemberUserId;
    [NotMapped]
    public ApplicationUser Member => PTPurchase.Member;
    public ApplicationUser PTUser { get; set; } = null!;
}

