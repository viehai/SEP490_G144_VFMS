using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Trainer review submitted by a Member (UC-67).
/// All content is screened against <see cref="BannedWordList"/> (GBR-66).
/// Manager may reply to feedback (UC-68).
/// </summary>
public class TrainerReview : BaseEntity
{
    public string TrainerUserId { get; set; } = string.Empty;
    public string MemberUserId { get; set; } = string.Empty;
    public int Rating { get; set; }   // 1-5
    public string? Comment { get; set; }
    public TrainerReviewStatus ReviewStatus { get; set; } = TrainerReviewStatus.Pending;
    public string? ManagerReply { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public bool IsVisible { get; set; } = false;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser TrainerUser { get; set; } = null!;
    public ApplicationUser MemberUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
}

