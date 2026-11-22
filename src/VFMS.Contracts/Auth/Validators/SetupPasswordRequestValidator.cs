using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Auth.Validators;

public sealed class SetupPasswordRequestValidator : AbstractValidator<SetupPasswordRequest>
{
    public SetupPasswordRequestValidator()
    {
        PasswordPolicyRules.ApplyHexToken(
            RuleFor(x => x.Token),
            "The password setup token is invalid.");

        PasswordPolicyRules.ApplyPassword(RuleFor(x => x.NewPassword));

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required.")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
