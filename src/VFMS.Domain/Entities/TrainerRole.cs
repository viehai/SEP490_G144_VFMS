using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// The kind of trainer: PT or Yoga (yoga instructor). Each maps to the application role its trainers hold.
/// </summary>
public class TrainerRole : BaseEntity
{
    public const string PT = "PT";
    public const string Yoga = "Yoga";

    public string RoleName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationRole Role { get; set; } = null!;
    public ICollection<TrainerProfile> TrainerProfiles { get; set; } = [];
    public ICollection<TrainerApplication> TrainerApplications { get; set; } = [];
}
