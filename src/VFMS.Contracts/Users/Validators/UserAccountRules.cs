using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

internal static class UserAccountRules
{
    public static readonly string[] StaffRoles = ["PT", "YogaInstructor", "Staff", "Manager"];

    public static void Apply<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> fullName,
        System.Linq.Expressions.Expression<Func<T, string>> email,
        System.Linq.Expressions.Expression<Func<T, string>> phoneNumber,
        System.Linq.Expressions.Expression<Func<T, string>> password,
        System.Linq.Expressions.Expression<Func<T, string>> confirmPassword)
    {
        validator.RuleFor(fullName)
            .NotEmpty().MaximumLength(256);

        validator.RuleFor(email)
            .NotEmpty().EmailAddress().MaximumLength(256);

        validator.RuleFor(phoneNumber)
            .NotEmpty().MaximumLength(20)
            .Matches(@"^[0-9+\-\s()]{7,20}$");

        validator.RuleFor(password)
            .NotEmpty().MinimumLength(8).MaximumLength(32)
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");

        validator.RuleFor(confirmPassword)
            .NotEmpty()
            .Equal(password).WithMessage("Passwords do not match.");
    }
}
