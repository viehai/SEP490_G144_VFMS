using FluentValidation;

namespace VFMS.Contracts.Workout.Validators;

public static class WorkoutRules
{
    public static readonly string[] Goals = ["WeightLoss", "MuscleGain", "Endurance", "Flexibility", "GeneralFitness"];
    public static readonly string[] Levels = ["Beginner", "Intermediate", "Advanced"];
    public static readonly string[] Genders = ["Male", "Female", "Other"];

    public static bool IsOneOf(string? value, string[] allowed) =>
        value is not null && allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed class FitnessSurveyRequestValidator : AbstractValidator<FitnessSurveyRequest>
{
    public FitnessSurveyRequestValidator()
    {
        RuleFor(x => x.WeightKg).InclusiveBetween(20, 300).WithMessage("Please enter a valid number between 20 and 300.");
        RuleFor(x => x.HeightCm).InclusiveBetween(100, 250).WithMessage("Please enter a valid number between 100 and 250.");
        RuleFor(x => x.FitnessGoal).Must(g => WorkoutRules.IsOneOf(g, WorkoutRules.Goals))
            .WithMessage("Choose a valid fitness goal.");
        RuleFor(x => x.FitnessLevel).Must(l => WorkoutRules.IsOneOf(l, WorkoutRules.Levels))
            .WithMessage("Choose a valid fitness level.");
        RuleFor(x => x.WorkoutFrequencyPerWeek).InclusiveBetween(1, 7).WithMessage("Please enter a valid number between 1 and 7.");
        RuleFor(x => x.Gender).Must(g => g is null || WorkoutRules.IsOneOf(g, WorkoutRules.Genders))
            .WithMessage("Choose a valid gender.");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || (d.Value.Year >= 1900 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10)))
            .WithMessage("Please enter a valid date of birth.");
        RuleFor(x => x.HealthConditions).MaximumLength(500).WithMessage("Input exceeds maximum allowed length of 500 characters.");
    }
}

public sealed class UpdatePlanItemRequestValidator : AbstractValidator<UpdatePlanItemRequest>
{
    public UpdatePlanItemRequestValidator()
    {
        RuleFor(x => x.ExerciseId).GreaterThan(0).WithMessage("The Exercise field is required.");
        RuleFor(x => x.Sets).InclusiveBetween(1, 20).When(x => x.Sets is not null);
        RuleFor(x => x.Reps).InclusiveBetween(1, 100).When(x => x.Reps is not null);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 180).When(x => x.DurationMinutes is not null);
        RuleFor(x => x).Must(x => x.Reps is not null || x.DurationMinutes is not null)
            .WithName("Reps").WithMessage("Enter reps or a duration.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
