using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class MembershipPlanRequestValidator : AbstractValidator<MembershipPlanRequest>
{
    public MembershipPlanRequestValidator()
    {
        RuleFor(x => x.MaxFreezeCount).GreaterThanOrEqualTo(0).WithMessage("Số lượt phải từ 0 trở lên.");
        RuleFor(x => x.MinFreezeDaysPerRequest).GreaterThan(0).WithMessage("Thời gian tối thiểu phải từ 1 ngày.");
        RuleFor(x => x.MaxFreezeDaysPerRequest).GreaterThan(0).WithMessage("Thời gian tối đa phải từ 1 ngày.");
        RuleFor(x => x.MaxFreezeDaysPerRequest).GreaterThanOrEqualTo(x => x.MinFreezeDaysPerRequest)
            .When(x => x.MinFreezeDaysPerRequest.HasValue && x.MaxFreezeDaysPerRequest.HasValue)
            .WithMessage("Số ngày tối đa phải bằng hoặc lớn hơn số ngày tối thiểu.");
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Nhập tên gói hội viên.")
            .Must(name => name is null || name.Trim().Length <= 256)
            .WithMessage("Tên gói hội viên không được vượt quá 256 ký tự.");
        RuleFor(x => x.Description).MaximumLength(4000).WithMessage("Mô tả không được vượt quá 4000 ký tự.");
        RuleFor(x => x.TargetAudience).MaximumLength(4000).WithMessage("Đối tượng phù hợp không được vượt quá 4000 ký tự.");
        RuleFor(x => x.DurationValue)
            .Must((request, value) => IsValidDuration(value ?? request.DurationInDays,
                value.HasValue ? request.DurationUnit : MembershipDurationUnit.Day))
            .WithMessage("Nhập thời hạn gói bằng số nguyên dương trong phạm vi ngày được hỗ trợ.");
        RuleFor(x => x.DurationUnit).IsInEnum().WithMessage("Chọn đơn vị thời hạn là ngày hoặc tháng.");
        RuleFor(x => x.FreezeAllowanceOverrideUnit).IsInEnum().WithMessage("Chọn đơn vị bảo lưu là ngày hoặc tháng.");
        RuleFor(x => x.FreezeAllowanceOverrideValue)
            .Must((request, value) => value is null || MembershipFreezeDuration.TryToDays(value.Value, request.FreezeAllowanceOverrideUnit, out _))
            .WithMessage("Nhập tổng thời gian bảo lưu từ 0 trở lên trong phạm vi ngày được hỗ trợ.");
        RuleFor(x => x.Price)
            .NotNull().WithMessage("Nhập giá gói hội viên.")
            .Must(price => price is null ||
                (price >= 0m && price <= 9_999_999_999_999_999.99m && decimal.Round(price.Value, 2) == price.Value))
            .WithMessage("Giá phải từ 0 đồng, có tối đa 18 chữ số, gồm tối đa hai chữ số thập phân.");
        RuleFor(x => x.Status)
            .Must(MembershipPlanRules.IsStatus).WithMessage(MembershipPlanRules.StatusMessage);
        RuleFor(x => x.ServiceType)
            .Must(MembershipPlanRules.IsServiceType).WithMessage(MembershipPlanRules.ServiceTypeMessage);
    }

    private static bool IsValidDuration(int? value, MembershipDurationUnit unit)
    {
        var maximum = unit == MembershipDurationUnit.Month
            ? (DateOnly.MaxValue.Year - 1) * 12 + 11 : DateOnly.MaxValue.DayNumber + 1;
        return value > 0 && value <= maximum;
    }
}

public sealed class MembershipPlanStatusRequestValidator : AbstractValidator<MembershipPlanStatusRequest>
{
    public MembershipPlanStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(MembershipPlanRules.IsStatus).WithMessage(MembershipPlanRules.StatusMessage);
    }
}

public sealed class MembershipPlanListRequestValidator : AbstractValidator<MembershipPlanListRequest>
{
    public MembershipPlanListRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(256);
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || MembershipPlanRules.IsStatus(status))
            .WithMessage(MembershipPlanRules.StatusMessage);
        RuleFor(x => x.ServiceType)
            .Must(type => string.IsNullOrWhiteSpace(type) || MembershipPlanRules.IsServiceType(type))
            .WithMessage(MembershipPlanRules.ServiceTypeMessage);
        RuleFor(x => x.DurationUnit).IsInEnum().WithMessage("Chọn đơn vị thời hạn là ngày hoặc tháng.");
        RuleFor(x => x.FreezeAllowanceFilter).IsInEnum().WithMessage("Bộ lọc quyền lợi bảo lưu không hợp lệ.");
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal static class MembershipPlanRules
{
    public const string StatusMessage = "Chọn trạng thái đang bán hoặc ngừng bán.";
    public const string ServiceTypeMessage = "Chọn loại dịch vụ Gym hoặc Yoga.";

    public static bool IsServiceType(string? type) =>
        string.Equals(type?.Trim(), MembershipPlanServiceTypes.Gym, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type?.Trim(), MembershipPlanServiceTypes.Yoga, StringComparison.OrdinalIgnoreCase);

    public static bool IsStatus(string? status) =>
        string.Equals(status?.Trim(), MembershipPlanStatuses.Active, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), MembershipPlanStatuses.Inactive, StringComparison.OrdinalIgnoreCase);
}
