using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class MembershipFreezeRequestValidator : AbstractValidator<MembershipFreezeRequest>
{
    public MembershipFreezeRequestValidator()
    {
        RuleFor(request => request.MembershipId).NotNull().GreaterThan(0).WithMessage("Chọn gói hội viên hợp lệ.");
        RuleFor(request => request.StartDate).NotNull().WithMessage("Chọn ngày bắt đầu bảo lưu.");
        RuleFor(request => request.RequestedDays).NotNull().GreaterThan(0).WithMessage("Số ngày bảo lưu phải lớn hơn 0.");
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Vui lòng nhập lý do bảo lưu.")
            .MaximumLength(4000).WithMessage("Lý do bảo lưu tối đa 4000 ký tự.");
    }
}
