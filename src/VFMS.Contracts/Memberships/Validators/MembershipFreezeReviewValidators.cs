using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class RejectMembershipFreezeRequestValidator : AbstractValidator<RejectMembershipFreezeRequest>
{
    public RejectMembershipFreezeRequestValidator()
    {
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Vui lòng nhập lý do từ chối.")
            .MaximumLength(4000).WithMessage("Lý do từ chối tối đa 4000 ký tự.");
    }
}

public sealed class MembershipFreezeReviewListRequestValidator : AbstractValidator<MembershipFreezeReviewListRequest>
{
    public MembershipFreezeReviewListRequestValidator()
    {
        RuleFor(request => request.Type).Must(type => type is null or "" or
            MembershipFreezeReviewTypes.Freeze or MembershipFreezeReviewTypes.EarlyUnfreeze);
        RuleFor(request => request.Status).Must(IsStatus);
        RuleFor(request => request.Search).MaximumLength(256);
        RuleFor(request => request.ToDate).GreaterThanOrEqualTo(request => request.FromDate)
            .When(request => request.FromDate.HasValue && request.ToDate.HasValue);
        RuleFor(request => request.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
    }

    public static bool IsStatus(string? status) => status is null or "" or "All" or "Pending" or "Approved" or "Active" or
        "Completed" or "Rejected" or "Cancelled" or "Expired" or "History";
}
