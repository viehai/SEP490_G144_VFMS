using FluentValidation;

namespace VFMS.Contracts.Common;

public static class PasswordPolicyRules
{
    public const string HexTokenPattern = @"\A[A-F0-9]{64}\z";

    public static IRuleBuilderOptions<T, string> ApplyPassword<T>(IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(32).WithMessage("Password must not exceed 32 characters.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");

    public static IRuleBuilderOptions<T, string> ApplyHexToken<T>(
        IRuleBuilder<T, string> ruleBuilder,
        string message) =>
        ruleBuilder
            .NotEmpty().WithMessage(message)
            .Matches(HexTokenPattern).WithMessage(message);
}
