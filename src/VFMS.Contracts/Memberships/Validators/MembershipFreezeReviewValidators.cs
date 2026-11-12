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
        RuleFor(request => request.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
    }
}
