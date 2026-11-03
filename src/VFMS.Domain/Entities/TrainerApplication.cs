using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Trainer recruitment application submitted by a Guest or Customer.
/// Submitting does NOT create an account (GBR-02).
/// Only upon <c>Hired</c> status does the system provision a PT user account (UC-23).
/// Enforces 30-day duplicate cooldown per CCCD/Email (GBR-03).
/// </summary>
public class TrainerApplication : BaseEntity
{
    public string? ApplicantUserId { get; set; }   // null if submitted as Guest
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string CitizenId { get; set; } = string.Empty;
    /// <summary>URL to uploaded certificate/CV attachment (≤ 5 MB PDF/JPG/PNG, GBR-01).</summary>
    public string ApplicationFileUrl { get; set; } = string.Empty;
    public TrainerApplicationStatus ApplicationStatus { get; set; } = TrainerApplicationStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public int SubmissionAttempt { get; set; } = 1;
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser? Applicant { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
}

