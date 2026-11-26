using FluentValidation;

namespace VFMS.Contracts.Users.Validators;

public sealed class UpdateUserRolesRequestValidator : AbstractValidator<UpdateUserRolesRequest>
{
    public UpdateUserRolesRequestValidator()
    {
        RuleFor(x => x.Roles)
            .Cascade(CascadeMode.Stop)
            .Must(roles => roles is { Count: 1 })
            .WithMessage("Vui lòng chọn đúng một vai trò.")
            .Must(roles => UpdateUserRolesRequest.Assignable.Contains(roles.Single(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Vai trò được chọn không hợp lệ.");
    }
}
