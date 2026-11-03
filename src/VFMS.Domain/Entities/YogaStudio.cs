using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Yoga studio inside the gym. Each studio has a unique name per gym (GBR-71).
/// Cannot be deactivated if future active sessions exist (GBR-72).
/// </summary>
public class YogaStudio : BaseEntity
{
    public int GymId { get; set; }
    public string StudioName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Navigation Properties ──────────────────────────────────
    public Gym Gym { get; set; } = null!;
    public ICollection<YogaClassSchedule> ClassSchedules { get; set; } = [];
}

