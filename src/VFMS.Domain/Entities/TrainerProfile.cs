using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Public-facing PT / Yoga Instructor profile.
/// Bio and avatar can be self-updated; certificates/specialties require Manager approval (GBR-05).
/// All content is screened against <see cref="BannedWordList"/>.
/// </summary>
public class TrainerProfile : BaseEntity
{
    public string PTUserId { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CertificateUrl { get; set; }
    public TrainerLevel Level { get; set; }
    /// <summary>Commission tier identifier (configured upon hiring, GBR-04).</summary>
    public string? CommissionTier { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser PTUser { get; set; } = null!;
    public ICollection<PTAvailability> Availabilities { get; set; } = [];
    public ICollection<PTPurchase> PTPurchases { get; set; } = [];
    public ICollection<TrainerReview> Reviews { get; set; } = [];
    public ICollection<YogaClass> AssignedYogaClasses { get; set; } = [];
}

