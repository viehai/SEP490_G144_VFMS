using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Application role (e.g., Customer, Member, PT, YogaInstructor, Staff, Manager, Admin).
/// </summary>
public class ApplicationRole : BaseEntity
{
    public string RoleName { get; set; } = string.Empty;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<ApplicationUserRole> UserRoles { get; set; } = [];
}

