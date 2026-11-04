using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

public sealed class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().MaximumLength(256);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().MaximumLength(20)
            .Matches(@"^[0-9+\-\s()]{7,20}$");

        RuleFor(x => x.AvatarUrl)
            .MaximumLength(2048)
            .Must(url => string.IsNullOrWhiteSpace(url) ||
                Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
            .WithMessage("Avatar URL must be a valid HTTP or HTTPS URL.");
    }
}
