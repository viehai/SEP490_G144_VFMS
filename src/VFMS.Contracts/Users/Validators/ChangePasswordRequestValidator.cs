using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Users.Validators;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.OldPassword)
            .NotEmpty();

        PasswordPolicyRules.ApplyPassword(RuleFor(x => x.NewPassword))
            .NotEqual(x => x.OldPassword).WithMessage("New password must differ from the old password.");
    }
}
