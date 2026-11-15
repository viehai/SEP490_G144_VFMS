using FluentValidation;

namespace VFMS.Contracts.Memberships.Validators;

public sealed class RenewMembershipRequestValidator : AbstractValidator<RenewMembershipRequest>
{
    public RenewMembershipRequestValidator() =>
        RuleFor(x => x.MembershipId).GreaterThan(0);
}

public sealed class MembershipPurchaseRequestValidator : AbstractValidator<MembershipPurchaseRequest>
{
    public MembershipPurchaseRequestValidator()
    {
        RuleFor(x => x.Items).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(items => items.Count <= 50).WithMessage("Mỗi đơn hàng có tối đa 50 gói tập.");
        RuleForEach(x => x.Items).NotNull().SetValidator(new MembershipPurchaseItemRequestValidator());
        RuleFor(x => x.PaymentMethod)
            .Must(method => method is MembershipPaymentMethods.VietQr or MembershipPaymentMethods.Cash)
            .WithMessage("Chọn VietQR hoặc tiền mặt.");
    }
}

public sealed class MembershipPurchaseItemRequestValidator : AbstractValidator<MembershipPurchaseItemRequest>
{
    public MembershipPurchaseItemRequestValidator()
    {
        RuleFor(x => x.PlanId).GreaterThan(0);
        RuleFor(x => x.BeneficiaryEmail).MaximumLength(256).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.BeneficiaryEmail));
    }
}

public sealed class ConfirmMembershipCashRequestValidator : AbstractValidator<ConfirmMembershipCashRequest>
{
    public ConfirmMembershipCashRequestValidator() =>
        RuleFor(x => x.AmountReceived).GreaterThan(0);
}

public sealed class ApplyMembershipVoucherRequestValidator : AbstractValidator<ApplyMembershipVoucherRequest>
{
    public ApplyMembershipVoucherRequestValidator() => RuleFor(request => request.VoucherCode)
        .NotEmpty().WithMessage("Nhập mã voucher.").MaximumLength(50);
}
