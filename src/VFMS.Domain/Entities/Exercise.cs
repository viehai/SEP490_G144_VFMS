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

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<PlanItem> PlanItems { get; set; } = [];
}

