using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Member's physical profile used for AI workout plan personalisation (GBR-93).
/// All PII is stripped before calling Gemini API (API-02).
/// </summary>
public class PhysicalProfile : BaseEntity
{
    public string MemberUserId { get; set; } = string.Empty;
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    public string? FitnessGoal { get; set; }
    public string? FitnessLevel { get; set; }
    public string? HealthConditions { get; set; }
    public int? WorkoutFrequencyPerWeek { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Member { get; set; } = null!;
    public ICollection<WorkoutPlan> WorkoutPlans { get; set; } = [];
}

