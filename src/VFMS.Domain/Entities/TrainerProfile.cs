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
    public string Specialty { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string CertificatesJson { get; set; } = "[]";
    public string? AvatarJson { get; set; }
    public string PTUserId { get; set; } = string.Empty;
    /// <summary>PT or Yoga; null only for rows created before trainer roles existed.</summary>
    public int? TrainerRoleId { get; set; }
    /// <summary>Citizen ID (CCCD) copied from the hired application; verified on ProfileUpdate (UC-3.2, UC-3.3).</summary>
    public string? CitizenId { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CertificateUrl { get; set; }
    public TrainerLevel Level { get; set; }
    /// <summary>Commission tier identifier (configured upon hiring, GBR-04).</summary>
    public string? CommissionTier { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AcceptingNewMembers { get; set; }
    /// <summary>First day of the current 'Not Accepting' period; null while accepting (GBR-98).</summary>
    public DateOnly? NotAcceptingSince { get; set; }
    /// <summary>GBR-98: start of the 60-day count; moved forward each day the trainer still has a running contract.</summary>
    public DateOnly? NotAcceptingCountFrom { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser PTUser { get; set; } = null!;
    public TrainerRole? TrainerRole { get; set; }
    public ICollection<PTAvailability> Availabilities { get; set; } = [];
    public ICollection<PTPurchase> PTPurchases { get; set; } = [];
    public ICollection<TrainerReview> Reviews { get; set; } = [];
}

