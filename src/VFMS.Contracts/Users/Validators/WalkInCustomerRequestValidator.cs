using FluentValidation;
using VFMS.Contracts.Common;

namespace VFMS.Contracts.Users.Validators;

public sealed class WalkInCustomerRequestValidator : AbstractValidator<WalkInCustomerRequest>
{
    public WalkInCustomerRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().MaximumLength(256);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().Must(UserIdentityRules.IsValidPhone)
            .WithMessage("Invalid phone number format.");

        RuleFor(x => x.Email)
            .NotEmpty().Must(UserIdentityRules.IsValidEmail)
            .WithMessage("Invalid email format.");
    }
}
