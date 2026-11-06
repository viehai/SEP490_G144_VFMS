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
    /// <summary>InBody body-fat percentage (optional, UC-49).</summary>
    public decimal? BodyFatPercent { get; set; }
    /// <summary>InBody skeletal muscle mass in kg (optional, UC-49).</summary>
    public decimal? SkeletalMuscleMassKg { get; set; }
    /// <summary>When the member last consented to AI processing of the survey (SRS 2.3 explicit PDPL consent).</summary>
    public DateTime? AiConsentGrantedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Member { get; set; } = null!;
    public ICollection<WorkoutPlan> WorkoutPlans { get; set; } = [];
}

