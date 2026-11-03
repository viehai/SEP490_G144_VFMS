using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// In-app notification delivered to a user.
/// Retained for 90 days (GBR-21).
/// </summary>
public class Notification : BaseEntity
{
    public string RecipientUserId { get; set; } = string.Empty;
    public string? SenderUserId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public DateTime NotifiedAt { get; set; }
    /// <summary>Deep-link reference (entity type + id) for navigation.</summary>
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Recipient { get; set; } = null!;
    public ApplicationUser? Sender { get; set; }
}

