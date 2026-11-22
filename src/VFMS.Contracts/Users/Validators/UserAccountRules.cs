using FluentValidation;
using VFMS.Contracts.Common;

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
            .NotEmpty().Must(UserIdentityRules.IsValidEmail).WithMessage("Invalid email format.");

        validator.RuleFor(phoneNumber)
            .NotEmpty().Must(UserIdentityRules.IsValidPhone).WithMessage("Invalid phone number format.");

        PasswordPolicyRules.ApplyPassword(validator.RuleFor(password));

        validator.RuleFor(confirmPassword)
            .NotEmpty()
            .Equal(password).WithMessage("Passwords do not match.");
    }
}
