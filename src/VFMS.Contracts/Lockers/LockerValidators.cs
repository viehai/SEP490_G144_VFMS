using FluentValidation;

namespace VFMS.Contracts.Lockers;

// Syntax rules only; payment method values, amounts and rental state are checked in the Application layer.
public sealed class CounterLockerRentalRequestValidator : AbstractValidator<CounterLockerRentalRequest>
{
    public CounterLockerRentalRequestValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.PaymentMethod).NotEmpty();
    }
}

public sealed class LockerRenewalRequestValidator : AbstractValidator<LockerRenewalRequest>
{
    public LockerRenewalRequestValidator() => RuleFor(x => x.PaymentMethod).MaximumLength(20);
}

public sealed class LockerSettlementRequestValidator : AbstractValidator<LockerSettlementRequest>
{
    public LockerSettlementRequestValidator() => RuleFor(x => x.PaymentMethod).MaximumLength(20);
}

public sealed class LockerTerminationRequestValidator : AbstractValidator<LockerTerminationRequest>
{
    public LockerTerminationRequestValidator()
    {
        RuleFor(x => x.SettlementOrderNumber).MaximumLength(50);
        RuleFor(x => x.RefundMethod).MaximumLength(20);
        RuleFor(x => x.RefundReference).MaximumLength(100);
    }
}

public sealed class LockerRefundRequestValidator : AbstractValidator<LockerRefundRequest>
{
    public LockerRefundRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

public sealed class LockerInventoryItemValidator : AbstractValidator<LockerInventoryItem>
{
    public LockerInventoryItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10000);
    }
}

public sealed class LockerClearanceRequestValidator : AbstractValidator<LockerClearanceRequest>
{
    public LockerClearanceRequestValidator()
    {
        RuleFor(x => x.Inventory).NotNull().Must(items => items is null || items.Length <= 100)
            .WithMessage("Danh sách kiểm kê tối đa 100 món.");
        RuleForEach(x => x.Inventory).SetValidator(new LockerInventoryItemValidator());
    }
}
