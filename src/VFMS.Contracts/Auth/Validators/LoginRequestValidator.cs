using FluentValidation;

namespace VFMS.Contracts.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email or phone number is required.")
            .Must(identifier => identifier is not null &&
                ((System.Net.Mail.MailAddress.TryCreate(identifier, out var address) &&
                    string.Equals(address.Address, identifier, StringComparison.OrdinalIgnoreCase)) ||
                    System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[0-9+\-\s()]{7,20}$")))
            .WithMessage("Enter a valid email address or phone number.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
