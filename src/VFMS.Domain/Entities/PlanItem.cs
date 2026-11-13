using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single exercise entry within a <see cref="WorkoutPlan"/>.
/// </summary>
public class PlanItem : BaseEntity
{
    public int PlanId { get; set; }
    public int ExerciseId { get; set; }
    /// <summary>Machine or equipment the member uses for this exercise (ERD PlanItem.EquipmentId, UC-59 swap).</summary>
    public int? EquipmentId { get; set; }
    public int DayNumber { get; set; }
    public int OrderInDay { get; set; }
    public int? Sets { get; set; }
    public int? Reps { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Note { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public WorkoutPlan WorkoutPlan { get; set; } = null!;
    public Exercise Exercise { get; set; } = null!;
    public Equipment? Equipment { get; set; }
    /// <summary>Daily progress logs (UC-50); "done this week" is derived from them.</summary>
    public ICollection<WorkoutLog> Logs { get; set; } = [];
}

