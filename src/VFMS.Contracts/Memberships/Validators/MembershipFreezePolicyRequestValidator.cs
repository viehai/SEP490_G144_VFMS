using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class MembershipFreezePolicyRequestValidator : AbstractValidator<MembershipFreezePolicyRequest>
{
    public MembershipFreezePolicyRequestValidator()
    {
        RuleFor(request => request.DefaultMaxFreezeCount).GreaterThanOrEqualTo(0).WithMessage("Số lượt phải từ 0 trở lên.");
        RuleFor(request => request.MinimumFreezeDays)
            .Must((request, value) => Valid(value, request.MinimumFreezeDaysUnit, 1))
            .WithMessage("Nhập thời gian bảo lưu tối thiểu hợp lệ, ít nhất 1 ngày.");
        RuleFor(request => request.MaximumFreezeDays)
            .Must((request, value) => Valid(value, request.MaximumFreezeDaysUnit, 1))
            .WithMessage("Nhập thời gian bảo lưu tối đa hợp lệ, ít nhất 1 ngày.");
        RuleFor(request => request.MinimumRequestDays)
            .Must((request, value) => Valid(value, request.MinimumRequestDaysUnit, 0))
            .WithMessage("Nhập thời gian báo trước tối thiểu hợp lệ, từ 0 trở lên.");
        RuleFor(request => request.EligibilityPaidDays)
            .Must((request, value) => Valid(value, request.EligibilityPaidDaysUnit, 0))
            .WithMessage("Nhập thời gian tối thiểu kể từ ngày kích hoạt hợp lệ, từ 0 trở lên.");
        RuleFor(request => request.Quotas).Must(quotas => quotas is null or { Count: 0 })
            .WithMessage("Vui lòng thiết lập số lượt và thời gian bảo lưu mỗi lượt cho gói Gym.");
        RuleFor(request => request).Custom((request, context) =>
        {
            if (request.MinimumFreezeDays is not { } minimum ||
                !MembershipFreezeDuration.TryToDays(minimum, request.MinimumFreezeDaysUnit, out var minDays))
                return;
            if (request.MaximumFreezeDays is { } maximum &&
                MembershipFreezeDuration.TryToDays(maximum, request.MaximumFreezeDaysUnit, out var maxDays) && maxDays < minDays)
                context.AddFailure(nameof(request.MaximumFreezeDays), "Thời gian bảo lưu tối đa phải bằng hoặc lớn hơn thời gian tối thiểu.");
        });
    }

    private static bool Valid(int? value, MembershipDurationUnit unit, int minimum) =>
        value is { } amount && MembershipFreezeDuration.TryToDays(amount, unit, out var days) && days >= minimum;
}
