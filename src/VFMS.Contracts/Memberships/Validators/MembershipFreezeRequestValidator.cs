using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class MembershipFreezeRequestValidator : AbstractValidator<MembershipFreezeRequest>
{
    public MembershipFreezeRequestValidator()
    {
        RuleFor(request => request.MembershipId).NotNull().GreaterThan(0).WithMessage("Chọn gói hội viên hợp lệ.");
        RuleFor(request => request.StartDate).NotNull().WithMessage("Chọn ngày bắt đầu bảo lưu.");
        RuleFor(request => request.RequestedDays).NotNull().GreaterThan(0)
            .WithMessage("Chọn ngày kết thúc bảo lưu.").When(request => request.EndDate is null);
        RuleFor(request => request.EndDate).GreaterThanOrEqualTo(request => request.StartDate)
            .When(request => request.StartDate.HasValue && request.EndDate.HasValue)
            .WithMessage("Ngày kết thúc bảo lưu phải từ ngày bắt đầu trở đi.");
        RuleFor(request => request).Must(request => !request.EndDate.HasValue || !request.StartDate.HasValue ||
                !request.RequestedDays.HasValue || request.RequestedDays ==
                request.EndDate.Value.DayNumber - request.StartDate.Value.DayNumber + 1)
            .WithMessage("Số ngày bảo lưu không khớp với khoảng ngày đã chọn.");
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Vui lòng nhập lý do bảo lưu.")
            .MaximumLength(4000).WithMessage("Lý do bảo lưu tối đa 4000 ký tự.");
    }
}
