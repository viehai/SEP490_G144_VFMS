using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

public sealed class UpdateUserRolesRequestValidator : AbstractValidator<UpdateUserRolesRequest>
{
    public UpdateUserRolesRequestValidator()
    {
        RuleFor(x => x.Roles)
            .NotNull()
            .Must(roles => roles is { Count: > 0 })
            .WithMessage("At least one role is required.")
            .Must(roles => roles is not null && roles.All(role =>
                UserAccountRules.StaffRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
            .WithMessage("Roles may only contain PT, YogaInstructor, Staff, or Manager.")
            .Must(roles => roles is not null && roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == roles.Count)
            .WithMessage("Roles must not contain duplicates.");
    }
}
