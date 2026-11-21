using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email or phone number is required.")
            .Must(identifier => UserIdentityRules.IsValidEmail(identifier) || UserIdentityRules.IsValidPhone(identifier))
            .WithMessage("Enter a valid email address or phone number.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
