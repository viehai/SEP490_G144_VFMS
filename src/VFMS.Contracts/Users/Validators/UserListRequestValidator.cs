using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

public sealed class UserListRequestValidator : AbstractValidator<UserListRequest>
{
    private static readonly string[] FilterableRoles = ["Customer", "Member", "PT", "Staff", "Manager", "Admin"];
    private static readonly string[] AccountStatuses = ["Active", "Locked", "Inactive", "PendingVerification"];

    public UserListRequestValidator()
    {
        RuleFor(x => x.Role)
            .Must(role => string.IsNullOrWhiteSpace(role) ||
                FilterableRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Role filter is invalid.");

        RuleFor(x => x.AccountStatus)
            .Must(status => string.IsNullOrWhiteSpace(status) ||
                AccountStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Account status filter is invalid.");

        RuleFor(x => x.Search)
            .MaximumLength(100);

        RuleFor(x => x.Page)
            .InclusiveBetween(1, 1_000_000);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);
    }
}
