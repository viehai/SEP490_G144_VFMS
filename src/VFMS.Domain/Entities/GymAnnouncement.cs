using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Gym-wide announcement managed by a Manager.
/// Expiry ≤ 60 days; maximum 3 concurrent pinned announcements (GBR-22, GBR-23).
/// </summary>
public class GymAnnouncement : BaseEntity
{
    public int GymId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public bool IsPinned { get; set; } = false;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Navigation Properties ──────────────────────────────────
    public Gym Gym { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}

