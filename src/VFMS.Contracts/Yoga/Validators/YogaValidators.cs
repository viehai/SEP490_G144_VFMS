using FluentValidation;

namespace VFMS.Contracts.Yoga.Validators;

public static class YogaRules
{
    public static readonly string[] DifficultyLevels = ["Beginner", "Intermediate", "Advanced"];
    public const int MinSessionMinutes = 45;   // GBR-75
    public const int MaxSessionMinutes = 90;   // GBR-75

    public static bool IsDifficulty(string? value) =>
        value is not null && DifficultyLevels.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed class YogaStudioRequestValidator : AbstractValidator<YogaStudioRequest>
{
    public YogaStudioRequestValidator()
    {
        RuleFor(x => x.StudioName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("The Studio name field is required.")
            .MaximumLength(200);
        RuleFor(x => x.FloorNumber).InclusiveBetween(-5, 100)
            .WithMessage("Please enter a valid number between -5 and 100.");
        RuleFor(x => x.Capacity).InclusiveBetween(1, 200)
            .WithMessage("Please enter a valid number between 1 and 200.");
    }
}

public sealed class YogaClassTypeRequestValidator : AbstractValidator<YogaClassTypeRequest>
{
    public YogaClassTypeRequestValidator()
    {
        RuleFor(x => x.TypeName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("The Type name field is required.")
            .MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

/// <summary>GBR-73 completeness and GBR-75 duration.</summary>
public sealed class YogaClassRequestValidator : AbstractValidator<YogaClassRequest>
{
    public YogaClassRequestValidator()
    {
        RuleFor(x => x.YogaClassTypeId).GreaterThan(0).WithMessage("The Class type field is required.");
        RuleFor(x => x.ClassName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("The Class name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.InstructorUserId)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("The Instructor field is required.");
        RuleFor(x => x.DifficultyLevel)
            .Must(YogaRules.IsDifficulty).WithMessage("Difficulty must be Beginner, Intermediate or Advanced.");
        RuleFor(x => x.SessionsPerWeek).InclusiveBetween(1, 7)
            .WithMessage("Please enter a valid number between 1 and 7.");
        RuleFor(x => x.MinParticipants).GreaterThanOrEqualTo(1)
            .WithMessage("Minimum participants must be at least 1.");
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(x => x.MinParticipants)
            .WithMessage("Maximum participants must be greater than or equal to minimum participants.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(YogaRules.MinSessionMinutes, YogaRules.MaxSessionMinutes)
            .WithMessage("Please enter a valid number between 45 and 90.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");
        RuleFor(x => x.RegistrationDeadline)
            .Must((req, deadline) => DateOnly.FromDateTime(deadline.AddHours(7)) <= req.StartDate)
            .WithMessage("Registration deadline must be on or before the start date.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ImageUrl).MaximumLength(500);
    }
}

public sealed class YogaSessionRequestValidator : AbstractValidator<YogaSessionRequest>
{
    public YogaSessionRequestValidator()
    {
        RuleFor(x => x.YogaClassId).GreaterThan(0).WithMessage("The Class field is required.");
        RuleFor(x => x.YogaStudioId).GreaterThan(0).WithMessage("The Studio field is required.");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("End time must be after start time.");
        RuleFor(x => x)
            .Must(x => (x.EndTime - x.StartTime).TotalMinutes is >= YogaRules.MinSessionMinutes and <= YogaRules.MaxSessionMinutes)
            .When(x => x.EndTime > x.StartTime)
            .WithName("EndTime")
            .WithMessage("A session must last between 45 and 90 minutes.");
    }
}
