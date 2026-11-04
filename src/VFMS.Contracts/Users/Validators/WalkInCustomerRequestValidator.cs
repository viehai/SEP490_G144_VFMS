using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

public sealed class WalkInCustomerRequestValidator : AbstractValidator<WalkInCustomerRequest>
{
    public WalkInCustomerRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().MaximumLength(256);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().MaximumLength(20)
            .Matches(@"^[0-9+\-\s()]{7,20}$");

        RuleFor(x => x.Email)
            .MaximumLength(256)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
