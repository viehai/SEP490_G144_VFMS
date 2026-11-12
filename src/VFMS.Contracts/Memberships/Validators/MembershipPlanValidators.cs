using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class MembershipPlanRequestValidator : AbstractValidator<MembershipPlanRequest>
{
    public MembershipPlanRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The plan name is required.")
            .Must(name => name is null || name.Trim().Length <= 256)
            .WithMessage("The plan name must be at most 256 characters.");
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.DurationInDays)
            .NotNull().GreaterThan(0).WithMessage("The duration in days must be a positive integer.");
        RuleFor(x => x.Price)
            .NotNull()
            .Must(price => price is null ||
                (price >= 0m && price <= 9_999_999_999_999_999.99m && decimal.Round(price.Value, 2) == price.Value))
            .WithMessage("The price must be non-negative and fit in 18 digits with at most two decimal places.");
        RuleFor(x => x.Status)
            .Must(MembershipPlanRules.IsStatus).WithMessage(MembershipPlanRules.StatusMessage);
    }
}

public sealed class MembershipPlanStatusRequestValidator : AbstractValidator<MembershipPlanStatusRequest>
{
    public MembershipPlanStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(MembershipPlanRules.IsStatus).WithMessage(MembershipPlanRules.StatusMessage);
    }
}

public sealed class MembershipPlanListRequestValidator : AbstractValidator<MembershipPlanListRequest>
{
    public MembershipPlanListRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(256);
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || MembershipPlanRules.IsStatus(status))
            .WithMessage(MembershipPlanRules.StatusMessage);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal static class MembershipPlanRules
{
    public const string StatusMessage = "Status must be ACTIVE or INACTIVE.";

    public static bool IsStatus(string? status) =>
        string.Equals(status?.Trim(), MembershipPlanStatuses.Active, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), MembershipPlanStatuses.Inactive, StringComparison.OrdinalIgnoreCase);
}
