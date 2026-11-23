using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Users.Validators;

public sealed class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().MaximumLength(256);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().Must(UserIdentityRules.IsValidPhone).WithMessage("Invalid phone number format.");

        RuleFor(x => x.AvatarUrl)
            .MaximumLength(2048)
            .Must(url => string.IsNullOrWhiteSpace(url) ||
                Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
            .WithMessage("Avatar URL must be a valid HTTP or HTTPS URL.");

        RuleFor(x => x.DateOfBirth)
            .Must(dob => !dob.HasValue ||
                (dob.Value >= new DateOnly(1900, 1, 1) &&
                 dob.Value < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))))
            .WithMessage("Date of birth must be a valid date before today.");
    }
}
