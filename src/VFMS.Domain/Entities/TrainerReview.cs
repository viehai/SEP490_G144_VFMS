using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Review or gym/package feedback submitted by a Member.
/// All content is screened against <see cref="BannedWordList"/> (GBR-66).
/// </summary>
public class TrainerReview : BaseEntity
{
    public int? PTPurchaseId { get; set; }
    public PTPurchase? PTPurchase { get; set; }
    public int? PackageId { get; set; }
    public Package? Package { get; set; }
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
    public ApplicationUser? TrainerUser { get; set; }
    public ApplicationUser MemberUser { get; set; } = null!;
    public ApplicationUser? ReviewedByUser { get; set; }
}

