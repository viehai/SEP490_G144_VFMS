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
        RuleFor(x => x.WeightKg).InclusiveBetween(20, 300).WithMessage("Vui lòng nhập số hợp lệ từ 20 đến 300.");
        RuleFor(x => x.HeightCm).InclusiveBetween(100, 250).WithMessage("Vui lòng nhập số hợp lệ từ 100 đến 250.");
        RuleFor(x => x.FitnessGoal).Must(g => WorkoutRules.IsOneOf(g, WorkoutRules.Goals))
            .WithMessage("Vui lòng chọn mục tiêu tập luyện hợp lệ.");
        RuleFor(x => x.FitnessLevel).Must(l => WorkoutRules.IsOneOf(l, WorkoutRules.Levels))
            .WithMessage("Vui lòng chọn trình độ hợp lệ.");
        RuleFor(x => x.WorkoutFrequencyPerWeek).InclusiveBetween(1, 7).WithMessage("Vui lòng nhập số hợp lệ từ 1 đến 7.");
        RuleFor(x => x.Gender).Must(g => g is null || WorkoutRules.IsOneOf(g, WorkoutRules.Genders))
            .WithMessage("Vui lòng chọn giới tính hợp lệ.");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || (d.Value.Year >= 1900 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10)))
            .WithMessage("Vui lòng nhập ngày sinh hợp lệ.");
        RuleFor(x => x.BodyFatPercent).InclusiveBetween(3, 60).When(x => x.BodyFatPercent is not null)
            .WithMessage("Vui lòng nhập số hợp lệ từ 3 đến 60.");
        RuleFor(x => x.SkeletalMuscleMassKg).InclusiveBetween(10, 80).When(x => x.SkeletalMuscleMassKg is not null)
            .WithMessage("Vui lòng nhập số hợp lệ từ 10 đến 80.");
        RuleFor(x => x.HealthConditions).MaximumLength(500).WithMessage("Nội dung không được quá 500 ký tự.");
    }
}

public sealed class SetPlanItemCompletionRequestValidator : AbstractValidator<SetPlanItemCompletionRequest>
{
    public SetPlanItemCompletionRequestValidator()
    {
        RuleFor(x => x.SetsDone).InclusiveBetween(1, 20).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.SetsDone is not null);
        RuleFor(x => x.RepsDone).InclusiveBetween(1, 200).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.RepsDone is not null);
        RuleFor(x => x.WeightKg).InclusiveBetween(0, 500).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.WeightKg is not null);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 300).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.DurationMinutes is not null);
    }
}

public sealed class UpdatePlanItemRequestValidator : AbstractValidator<UpdatePlanItemRequest>
{
    public UpdatePlanItemRequestValidator()
    {
        RuleFor(x => x.ExerciseId).GreaterThan(0).WithMessage("Vui lòng chọn bài tập.");
        RuleFor(x => x.Sets).InclusiveBetween(1, 20).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.Sets is not null);
        RuleFor(x => x.Reps).InclusiveBetween(1, 100).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.Reps is not null);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 180).WithMessage("Vui lòng nhập số từ {From} đến {To}.").When(x => x.DurationMinutes is not null);
        RuleFor(x => x).Must(x => x.Reps is not null || x.DurationMinutes is not null)
            .WithName("Reps").WithMessage("Hãy nhập số lần lặp hoặc thời lượng.");
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Tối đa 500 ký tự.");
        RuleFor(x => x.EquipmentId).GreaterThan(0).When(x => x.EquipmentId is not null);
    }
}
