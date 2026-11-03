using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Join table linking <see cref="ApplicationUser"/> to <see cref="ApplicationRole"/> (many-to-many).
/// </summary>
public class ApplicationUserRole : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int RoleId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser User { get; set; } = null!;
    public ApplicationRole Role { get; set; } = null!;
}

