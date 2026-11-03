using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Logical zone grouping lockers (e.g., by floor and gender restriction).
/// </summary>
public class LockerZone : BaseEntity
{
    public int GymId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public GenderRestriction GenderRestriction { get; set; } = GenderRestriction.None;
    public int TotalLockers { get; set; }
    public string? Description { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Gym Gym { get; set; } = null!;
    public ICollection<Locker> Lockers { get; set; } = [];
}

