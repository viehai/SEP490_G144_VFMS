using FluentValidation;

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
        RuleFor(x => x.Token).NotEmpty().Matches("^[A-F0-9]{64}$")
            .WithMessage("The reset token is invalid.");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(32)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
        RuleFor(x => x.ConfirmPassword).NotEmpty().Equal(x => x.NewPassword)
            .WithMessage("Passwords do not match.");
    }
}
