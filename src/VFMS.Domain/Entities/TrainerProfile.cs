using System.ComponentModel.DataAnnotations.Schema;
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
    public int ExperienceYears { get; set; }
    public string CertificatesJson { get; set; } = "[]";
    public string? AvatarJson { get; set; }
    public string PTUserId { get; set; } = string.Empty;
    /// <summary>PT or Yoga; null only for rows created before trainer roles existed.</summary>
    public int? TrainerRoleId { get; set; }
    public string? Bio { get; set; }
    public TrainerLevel Level { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AcceptingNewMembers { get; set; }
    /// <summary>First day of the current accepting/not-accepting period (GBR-104).</summary>
    public DateOnly? ReadinessSince { get; set; }
    [NotMapped]
    public DateOnly? NotAcceptingSince
    {
        get => AcceptingNewMembers ? null : ReadinessSince;
        set { if (value != null || !AcceptingNewMembers) ReadinessSince = value; }
    }
    /// <summary>GBR-104: first day not yet included in the accumulated inactivity count.</summary>
    public DateOnly? NotAcceptingCountFrom { get; set; }
    /// <summary>Inactive days already counted, excluding time accepting members or serving a running contract.</summary>
    public int NotAcceptingAccumulatedDays { get; set; }
    /// <summary>First day of the current accepting period; ten consecutive days clear accumulated inactivity.</summary>
    [NotMapped]
    public DateOnly? AcceptingSince
    {
        get => AcceptingNewMembers ? ReadinessSince : null;
        set { if (value != null || AcceptingNewMembers) ReadinessSince = value; }
    }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser PTUser { get; set; } = null!;
    public TrainerRole? TrainerRole { get; set; }
}

