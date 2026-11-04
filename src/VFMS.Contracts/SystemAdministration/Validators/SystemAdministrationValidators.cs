using FluentValidation;

namespace VFMS.Contracts.SystemAdministration.Validators;

public sealed class BannedWordRequestValidator : AbstractValidator<BannedWordRequest>
{
    public BannedWordRequestValidator()
    {
        RuleFor(x => x.Word)
            .Must(word => !string.IsNullOrWhiteSpace(word)).WithMessage("The Word field is required.")
            .MaximumLength(100).WithMessage("Input exceeds maximum allowed length of 100 characters.");
    }
}

public sealed class BannedWordListRequestValidator : AbstractValidator<BannedWordListRequest>
{
    public BannedWordListRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class AuditLogListRequestValidator : AbstractValidator<AuditLogListRequest>
{
    public static readonly string[] ActionTypes =
        ["Create", "Update", "Delete", "Login", "Logout", "RoleChange", "AccountLock", "AccountUnlock", "PasswordChange"];

    public AuditLogListRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .OverridePropertyName("From")
            .WithMessage("The start date cannot be after the end date.");
        RuleFor(x => x.ActionType)
            .Must(type => string.IsNullOrWhiteSpace(type) ||
                ActionTypes.Contains(type.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Action type filter is invalid.");
        RuleFor(x => x.ActorUserId).MaximumLength(128);
        RuleFor(x => x.Entity).MaximumLength(100);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
