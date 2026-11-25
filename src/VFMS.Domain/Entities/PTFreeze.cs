using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// PT package freeze request.
/// Manager reviews and processes PT freeze requests (UC-34, GBR-15).
/// Approving cancels scheduled sessions inside the freeze window (GBR-17).
/// </summary>
public static class PTFreezeChangeTypes
{
    public const string Withdraw = "Withdraw";
    public const string EarlyResume = "EarlyResume";
}

public static class PTFreezeChangeStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public class PTFreeze : BaseEntity
{
    public int PTPurchaseId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateOnly RequestedStartDate { get; set; }
    public DateOnly RequestedEndDate { get; set; }
    public DateOnly? ActualStartDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }
    public PTFreezeRequestStatus RequestStatus { get; set; } = PTFreezeRequestStatus.Pending;
    public string? ReviewNote { get; set; }
    public string? Reason { get; set; }
    public string? SupportingDocumentUrl { get; set; }
    public int? MinimumDaysSnapshot { get; set; }
    public int? MaxDaysSnapshot { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    /// <summary>Trainer who takes over the remaining sessions when the freeze is approved (GBR-105).</summary>
    public string? ReplacementTrainerId { get; set; }
    /// <summary>Last in-app reminder sent to Managers about this pending request (UC 4.9).</summary>
    public DateTime? ManagerRemindedAt { get; set; }

    // ── Withdraw / early-resume requests (the Manager decides; see NewScope.txt, UC 4.8 steps 8.2 and 8.3) ──
    /// <summary><see cref="PTFreezeChangeTypes"/>: what the Member asked for while the freeze stays in force.</summary>
    public string? ChangeRequestType { get; set; }
    /// <summary><see cref="PTFreezeChangeStatuses"/>: Pending until the Manager decides.</summary>
    public string? ChangeRequestStatus { get; set; }
    public DateTime? ChangeRequestedAt { get; set; }
    /// <summary>The new Resume Date of an early-resume request.</summary>
    public DateOnly? ChangeResumeDate { get; set; }
    /// <summary>JSON of the confirmed early-resume request (resume dates and the chosen moves).</summary>
    public string? ChangePayloadJson { get; set; }
    public string? ChangeReviewedByUserId { get; set; }
    public DateTime? ChangeReviewedAt { get; set; }
    public string? ChangeReviewNote { get; set; }
    /// <summary>What the approved request did, shown to the Member (restored sessions and sessions that could not be).</summary>
    public string? ChangeOutcome { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PTPurchase PTPurchase { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
}

