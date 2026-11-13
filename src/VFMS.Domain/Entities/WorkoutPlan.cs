using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// AI-generated or fallback personalised workout plan (API-02, GBR-93–GBR-95).
/// Only one plan is Active at a time; generating a new plan archives the previous (GBR-94).
/// </summary>
public class WorkoutPlan : BaseEntity
{
    public string MemberUserId { get; set; } = string.Empty;
    public int? PhysicalProfileId { get; set; }
    public string? Name { get; set; }
    public WorkoutPlanStatus PlanStatus { get; set; } = WorkoutPlanStatus.Active;
    public bool IsTemplate { get; set; } = false;
    /// <summary>True when the plan was auto-assigned as a fallback (GBR-95).</summary>
    public bool IsFallback { get; set; } = false;
    public string? FitnessGoal { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string? GeneratedByAiModel { get; set; }
    /// <summary>Short nutrition advice for the goal (UC-49): from Gemini, or a standard text for fallback plans.</summary>
    public string? NutritionAdvice { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Member { get; set; } = null!;
    public PhysicalProfile? PhysicalProfile { get; set; }
    public ICollection<PlanItem> PlanItems { get; set; } = [];
}

