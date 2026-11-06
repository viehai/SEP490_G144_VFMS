namespace VFMS.Contracts.Workout;

/// <summary>
/// Fitness survey (UC-49 / UC-58). Goal: WeightLoss, MuscleGain, Endurance, Flexibility, GeneralFitness.
/// Level: Beginner, Intermediate, Advanced.
/// </summary>
public sealed record FitnessSurveyRequest(
    decimal WeightKg,
    decimal HeightCm,
    string FitnessGoal,
    string FitnessLevel,
    int WorkoutFrequencyPerWeek,
    string? Gender = null,
    DateOnly? DateOfBirth = null,
    string? HealthConditions = null
);

public sealed record FitnessSurveyResponse(
    decimal? WeightKg,
    decimal? HeightCm,
    string? FitnessGoal,
    string? FitnessLevel,
    int? WorkoutFrequencyPerWeek,
    string? Gender,
    DateOnly? DateOfBirth,
    string? HealthConditions,
    bool IsComplete
);

/// <summary>Generate a plan. The member must consent to sending anonymised survey data to Gemini (PDPL).</summary>
public sealed record GenerateWorkoutPlanRequest(bool ConsentToAiProcessing);

public sealed record WorkoutPlanItemResponse(
    int Id,
    int DayNumber,
    int OrderInDay,
    int ExerciseId,
    string ExerciseName,
    string? MuscleGroup,
    string? DifficultyLevel,
    string? ImageUrl,
    string? VideoUrl,
    int? Sets,
    int? Reps,
    int? DurationMinutes,
    string? Note,
    bool IsCompleted,
    DateTime? CompletedAt
);

public sealed record WorkoutPlanResponse(
    int Id,
    string? Name,
    string Status,
    string? FitnessGoal,
    bool IsFallback,
    string? GeneratedByAiModel,
    DateTime GeneratedAt,
    int CompletedItems,
    int TotalItems,
    IReadOnlyList<WorkoutPlanItemResponse> Items
);

public sealed record WorkoutPlanSummaryResponse(
    int Id,
    string? Name,
    string Status,
    string? FitnessGoal,
    bool IsFallback,
    DateTime GeneratedAt,
    int CompletedItems,
    int TotalItems
);

/// <summary>Mark an exercise done or not done (UC-50).</summary>
public sealed record SetPlanItemCompletionRequest(bool IsCompleted);

/// <summary>Swap an exercise or adjust its volume (UC-59 "change exercises or equipment").</summary>
public sealed record UpdatePlanItemRequest(int ExerciseId, int? Sets, int? Reps, int? DurationMinutes, string? Note = null);

public sealed record ExerciseResponse(int Id, string Name, string? MuscleGroup, string? DifficultyLevel, string? Description);
