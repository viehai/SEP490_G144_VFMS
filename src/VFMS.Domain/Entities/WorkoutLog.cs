using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// What the member actually did for one plan exercise on one gym-local day (UC-50 / UC-59):
/// sets, reps, weight lifted or cardio minutes. One log per plan item per day.
/// </summary>
public class WorkoutLog : BaseEntity
{
    public int PlanItemId { get; set; }
    public DateOnly LoggedOn { get; set; }
    public int? SetsDone { get; set; }
    public int? RepsDone { get; set; }
    public decimal? WeightKg { get; set; }
    public int? DurationMinutes { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PlanItem PlanItem { get; set; } = null!;
}
