using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class MembershipFreezePolicyRequestValidator : AbstractValidator<MembershipFreezePolicyRequest>
{
    public MembershipFreezePolicyRequestValidator()
    {
        RuleFor(request => request.MinimumFreezeDays).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Nhập số ngày bảo lưu tối thiểu.")
            .GreaterThan(0).WithMessage("Số ngày bảo lưu tối thiểu phải lớn hơn 0.");
        RuleFor(request => request.MaximumFreezeDays).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Nhập số ngày bảo lưu tối đa.")
            .GreaterThan(0).WithMessage("Số ngày bảo lưu tối đa phải lớn hơn 0.")
            .GreaterThanOrEqualTo(request => request.MinimumFreezeDays)
            .When(request => request.MinimumFreezeDays is > 0, ApplyConditionTo.CurrentValidator)
            .WithMessage("Số ngày bảo lưu tối đa phải bằng hoặc lớn hơn số ngày tối thiểu.");
        RuleFor(request => request.MinimumRequestDays).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Nhập số ngày cần báo trước.")
            .GreaterThanOrEqualTo(0).WithMessage("Số ngày cần báo trước không được âm.");
        RuleFor(request => request.EligibilityPaidDays).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Nhập số ngày đã thanh toán tối thiểu.")
            .GreaterThanOrEqualTo(0).WithMessage("Số ngày đã thanh toán tối thiểu không được âm.");
        RuleFor(request => request.Quotas).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Danh sách hạn mức bảo lưu là bắt buộc.")
            .Must(HaveUniqueTiers).WithMessage("Mỗi kỳ hạn gói chỉ được có một hạn mức bảo lưu.");
        RuleForEach(request => request.Quotas)
            .NotNull().WithMessage("Nhập hạn mức bảo lưu cho kỳ hạn gói.")
            .SetValidator(new TierQuotaValidator());
    }

    private static bool HaveUniqueTiers(IReadOnlyList<MembershipFreezeTierQuotaRequest>? quotas) =>
        quotas!.Where(quota => quota is { Duration: > 0, Unit: MembershipFreezeTierUnits.Days or MembershipFreezeTierUnits.Months })
            .GroupBy(quota => (quota.Duration, quota.Unit)).All(tier => tier.Count() == 1);

    private sealed class TierQuotaValidator : AbstractValidator<MembershipFreezeTierQuotaRequest>
    {
        public TierQuotaValidator()
        {
            RuleFor(quota => quota.Duration).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Nhập kỳ hạn gói.")
                .GreaterThan(0).WithMessage("Kỳ hạn gói phải lớn hơn 0.");
            RuleFor(quota => quota.Unit)
                .Must(unit => unit is MembershipFreezeTierUnits.Days or MembershipFreezeTierUnits.Months)
                .WithMessage("Chọn đơn vị ngày hoặc tháng.");
            RuleFor(quota => quota.MaximumCumulativeFreezeDays).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Nhập số ngày bảo lưu tối đa cho kỳ hạn gói.")
                .GreaterThanOrEqualTo(0).WithMessage("Hạn mức bảo lưu không được âm.");
        }
    }
}
