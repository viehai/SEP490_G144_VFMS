using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Auth.Validators;

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email)
        .NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        PasswordPolicyRules.ApplyHexToken(RuleFor(x => x.Token), "The reset token is invalid.");
        PasswordPolicyRules.ApplyPassword(RuleFor(x => x.NewPassword));
        RuleFor(x => x.ConfirmPassword).NotEmpty().Equal(x => x.NewPassword)
            .WithMessage("Passwords do not match.");
    }
}
