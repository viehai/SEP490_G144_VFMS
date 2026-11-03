using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Immutable read-only audit log of all significant system actions.
/// Must never be hard-deleted (GBR-33, GBR-65). Admin has read-only access.
/// </summary>
public class AuditLog : BaseEntity
{
    public string? ActorUserId { get; set; }
    public AuditActionType ActionType { get; set; }
    public string AuditEntity { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? IpAddress { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser? ActorUser { get; set; }
}

