using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(256).WithMessage("Full name must not exceed 256 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .Must(UserIdentityRules.IsValidEmail).WithMessage("Invalid email format.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Must(UserIdentityRules.IsValidPhone).WithMessage("Invalid phone number format.");

        PasswordPolicyRules.ApplyPassword(RuleFor(x => x.Password));

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}
