using FluentValidation;

namespace VFMS.Contracts.Lockers;

// Syntax rules only; payment method values, amounts and rental state are checked in the Application layer.
public sealed class CounterLockerRentalRequestValidator : AbstractValidator<CounterLockerRentalRequest>
{
    public CounterLockerRentalRequestValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty().WithMessage("Mã hội viên không được để trống.").MaximumLength(450).WithMessage("Mã hội viên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.PaymentMethod).NotEmpty().WithMessage("Phương thức thanh toán không được để trống.");
    }
}

public sealed class LockerRenewalRequestValidator : AbstractValidator<LockerRenewalRequest>
{
    public LockerRenewalRequestValidator() => RuleFor(x => x.PaymentMethod).MaximumLength(20).WithMessage("Phương thức thanh toán không được vượt quá {MaxLength} ký tự.");
}

public sealed class LockerSettlementRequestValidator : AbstractValidator<LockerSettlementRequest>
{
    public LockerSettlementRequestValidator() => RuleFor(x => x.PaymentMethod).MaximumLength(20).WithMessage("Phương thức thanh toán không được vượt quá {MaxLength} ký tự.");
}

public sealed class LockerTerminationRequestValidator : AbstractValidator<LockerTerminationRequest>
{
    public LockerTerminationRequestValidator()
    {
        RuleFor(x => x.SettlementOrderNumber).MaximumLength(50).WithMessage("Mã đơn tất toán không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.RefundMethod).MaximumLength(20).WithMessage("Phương thức hoàn tiền không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.RefundReference).MaximumLength(100).WithMessage("Mã tham chiếu hoàn tiền không được vượt quá {MaxLength} ký tự.");
    }
}

public sealed class LockerInventoryItemValidator : AbstractValidator<LockerInventoryItem>
{
    public LockerInventoryItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên không được để trống.").MaximumLength(200).WithMessage("Tên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10000).WithMessage("Số lượng phải từ {From} đến {To}.");
    }
}

public sealed class LockerClearanceRequestValidator : AbstractValidator<LockerClearanceRequest>
{
    public LockerClearanceRequestValidator()
    {
        RuleFor(x => x.Inventory).NotNull().WithMessage("Vui lòng cung cấp thông tin.").Must(items => items is null || items.Length <= 100)
            .WithMessage("Danh sách kiểm kê tối đa 100 món.");
        RuleForEach(x => x.Inventory).SetValidator(new LockerInventoryItemValidator());
    }
}

// UC 5.7 Step 3: zone name and locker number lengths; uniqueness is checked in the Application layer (MSG-48).
public sealed class LockerZoneRequestValidator : AbstractValidator<LockerZoneRequest>
{
    public LockerZoneRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên không được để trống.").MaximumLength(100).WithMessage("Tên không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.Floor).InclusiveBetween(-5, 100).WithMessage("Tầng phải từ {From} đến {To}.");
    }
}

public sealed class LockerCreateRequestValidator : AbstractValidator<LockerCreateRequest>
{
    public LockerCreateRequestValidator() => RuleFor(x => x.Number).NotEmpty().WithMessage("Số tủ không được để trống.").MaximumLength(20).WithMessage("Số tủ không được vượt quá {MaxLength} ký tự.");
}

// UC 5.7 Step 3.2, MSG-06
public sealed class LockerRulesRequestValidator : AbstractValidator<LockerRulesRequest>
{
    public LockerRulesRequestValidator() => RuleFor(x => x.Text).MaximumLength(4000).WithMessage("Nội quy không được vượt quá {MaxLength} ký tự.");
}
