using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        UserAccountRules.Apply(this, x => x.FullName, x => x.Email, x => x.PhoneNumber, x => x.Password, x => x.ConfirmPassword);

        RuleFor(x => x.RoleName)
            .Must(role => UserAccountRules.StaffRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Role must be PT, YogaInstructor, Staff, or Manager.");
    }
}
