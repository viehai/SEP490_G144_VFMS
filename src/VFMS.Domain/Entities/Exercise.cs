using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Exercise catalog entry used in workout plans.
/// </summary>
public class Exercise : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? MuscleGroup { get; set; }
    public string? DifficultyLevel { get; set; }
    public string? ImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Manager's default machine for this exercise. Stands for its machine name: any working unit with that
    /// name is used when the AI or the standard plan gives the exercise no machine.</summary>
    public int? EquipmentId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Equipment? Equipment { get; set; }
    public ICollection<PlanItem> PlanItems { get; set; } = [];
}

