using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Records locker damage, loss, or theft incidents.
/// </summary>
public class LockerIncidentLog : BaseEntity
{
    public int LockerId { get; set; }
    public string? MemberUserId { get; set; }
    public string? HandledByUserId { get; set; }
    public LockerIncidentType IncidentType { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public decimal? FineAmount { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Locker Locker { get; set; } = null!;
    public ApplicationUser? Member { get; set; }
    public ApplicationUser? HandledByUser { get; set; }
}

