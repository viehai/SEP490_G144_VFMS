using VFMS.Contracts.Common;
using VFMS.Contracts.Workout;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IWorkoutRepository : IAuditedRepository
{
    Task<PhysicalProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Exercise>> GetActiveExercisesAsync(CancellationToken cancellationToken = default);
    /// <summary>Template plan for a goal with items and exercises (GBR-95 fallback).</summary>
    Task<WorkoutPlan?> GetFallbackTemplateAsync(string fitnessGoal, CancellationToken cancellationToken = default);
    /// <summary>The member's active plans (normally one, GBR-94).</summary>
    Task<IReadOnlyList<WorkoutPlan>> GetActivePlansAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>Plan with items and exercises.</summary>
    Task<WorkoutPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutPlan>> GetPlanHistoryAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Anonymised survey data sent to the AI. No name, email, phone, user ID or birth date (PDPL).</summary>
public sealed record AnonymisedSurvey(
    int? Age,
    string? Gender,
    decimal WeightKg,
    decimal HeightCm,
    string FitnessGoal,
    string FitnessLevel,
    int WorkoutFrequencyPerWeek,
    string? HealthConditions,
    decimal? BodyFatPercent = null,
    decimal? SkeletalMuscleMassKg = null
);

public sealed record CatalogExercise(int Id, string Name, string? MuscleGroup, string? DifficultyLevel);

public sealed record GeneratedPlanItem(int DayNumber, int OrderInDay, int ExerciseId, int? Sets, int? Reps, int? DurationMinutes, string? Note);

public sealed record GeneratedPlan(string Name, IReadOnlyList<GeneratedPlanItem> Items, string? NutritionAdvice = null);

/// <summary>Gemini API (API-02). Returns null when the AI is unavailable, times out or replies with unusable data.</summary>
public interface IWorkoutPlanGenerator
{
    string ModelName { get; }
    Task<GeneratedPlan?> GenerateAsync(AnonymisedSurvey survey, IReadOnlyList<CatalogExercise> catalog, CancellationToken cancellationToken = default);
}

/// <summary>UC-49/UC-58 Generate and UC-50/UC-59 Manage Personalized Workout Plan.</summary>
public interface IWorkoutPlanService
{
    Task<Result<FitnessSurveyResponse>> GetSurveyAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<FitnessSurveyResponse>> SaveSurveyAsync(string userId, FitnessSurveyRequest request, CancellationToken cancellationToken = default);
    Task<Result<WorkoutPlanResponse>> GenerateAsync(string userId, GenerateWorkoutPlanRequest request, CancellationToken cancellationToken = default);
    Task<Result<WorkoutPlanResponse>> GetActivePlanAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<WorkoutPlanResponse>> GetPlanAsync(string userId, int planId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<WorkoutPlanSummaryResponse>>> GetHistoryAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<WorkoutPlanItemResponse>> SetItemCompletionAsync(string userId, int planId, int itemId, bool isCompleted, CancellationToken cancellationToken = default);
    Task<Result<WorkoutPlanItemResponse>> UpdateItemAsync(string userId, int planId, int itemId, UpdatePlanItemRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ExerciseResponse>>> GetExercisesAsync(CancellationToken cancellationToken = default);
}
