using FluentValidation;

namespace VFMS.Contracts.Promotions.Validators;

public abstract class PromotionRequestValidator<T> : AbstractValidator<T> where T : PromotionRequest
{
    protected PromotionRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().WithMessage("Nhập tên chương trình khuyến mãi.")
            .MaximumLength(256).WithMessage("Tên khuyến mãi tối đa 256 ký tự.");
        RuleFor(request => request.Code).NotEmpty().WithMessage("Nhập mã voucher.")
            .MaximumLength(50).WithMessage("Mã voucher tối đa 50 ký tự.");
        RuleFor(request => request.DiscountType).NotNull().WithMessage("Chọn loại giảm giá.")
            .IsInEnum().WithMessage("Loại giảm giá không hợp lệ.");
        RuleFor(request => request.DiscountValue).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Nhập giá trị giảm giá.")
            .GreaterThan(0).WithMessage("Giá trị giảm giá phải lớn hơn 0.")
            .PrecisionScale(18, 2, false).WithMessage("Giá trị giảm giá phải phù hợp định dạng tiền tệ (18,2).");
        RuleFor(request => request.DiscountValue).LessThanOrEqualTo(100)
            .When(request => request.DiscountType == PromotionDiscountType.Percentage)
            .WithMessage("Phần trăm giảm giá không được vượt quá 100%.");
        RuleFor(request => request.MinimumSpend).GreaterThanOrEqualTo(0)
            .WithMessage("Giá trị đơn hàng tối thiểu không được âm.")
            .PrecisionScale(18, 2, false).WithMessage("Giá trị đơn hàng tối thiểu không hợp lệ.");
        RuleFor(request => request.MaxDiscountAmount).NotNull()
            .When(request => request.DiscountType == PromotionDiscountType.Percentage)
            .WithMessage("Voucher phần trăm phải có mức giảm tối đa.");
        RuleFor(request => request.MaxDiscountAmount).GreaterThan(0)
            .WithMessage("Mức giảm tối đa phải lớn hơn 0.").PrecisionScale(18, 2, false)
            .WithMessage("Mức giảm tối đa không hợp lệ.");
        RuleFor(request => request.MaxRedemptions).GreaterThan(0)
            .WithMessage("Tổng lượt sử dụng phải lớn hơn 0 hoặc để trống nếu không giới hạn.");
        RuleFor(request => request.MaxRedemptionsPerUser).GreaterThan(0)
            .WithMessage("Số lượt tối đa mỗi tài khoản phải lớn hơn 0 hoặc để trống nếu không giới hạn.");
        RuleFor(request => request.Audience).IsInEnum().WithMessage("Đối tượng áp dụng không hợp lệ.");
        When(request => request.Audience == PromotionAudience.NewCustomers, () =>
        {
            RuleFor(request => request.NewCustomerDuration).NotNull().WithMessage("Nhập thời gian được xem là khách hàng mới.")
                .GreaterThan(0).WithMessage("Thời gian được xem là khách hàng mới phải là số nguyên dương.");
            RuleFor(request => request.NewCustomerUnit).NotNull().WithMessage("Chọn đơn vị ngày hoặc tháng.")
                .IsInEnum().WithMessage("Đơn vị thời gian phải là ngày hoặc tháng.");
        }).Otherwise(() =>
        {
            RuleFor(request => request.NewCustomerDuration).Null().WithMessage("Chỉ nhập thời gian cho ưu đãi khách hàng mới.");
            RuleFor(request => request.NewCustomerUnit).Null().WithMessage("Chỉ chọn đơn vị thời gian cho ưu đãi khách hàng mới.");
        });
        RuleFor(request => request.StartAt).NotNull().WithMessage("Nhập thời gian bắt đầu.")
            .Must(IsUtcTimestamp).WithMessage("Thời gian bắt đầu phải là dấu thời gian UTC hợp lệ.");
        RuleFor(request => request.EndAt).NotNull().WithMessage("Nhập thời gian kết thúc.")
            .Must(IsUtcTimestamp).WithMessage("Thời gian kết thúc phải là dấu thời gian UTC hợp lệ.")
            .GreaterThan(request => request.StartAt)
            .When(request => request.StartAt.HasValue, ApplyConditionTo.CurrentValidator)
            .WithMessage("Thời gian kết thúc phải sau thời gian bắt đầu.");
        RuleFor(request => request.IsEnabled).NotNull().WithMessage("Chọn trạng thái bật/tắt.");
    }

    private static bool IsUtcTimestamp(DateTime? value) =>
        value is null || value.Value != default && value.Value.Kind == DateTimeKind.Utc;
}

public sealed class CreatePromotionRequestValidator : PromotionRequestValidator<CreatePromotionRequest>;
public sealed class UpdatePromotionRequestValidator : PromotionRequestValidator<UpdatePromotionRequest>;

public sealed class SetPromotionEnabledRequestValidator : AbstractValidator<SetPromotionEnabledRequest>
{
    public SetPromotionEnabledRequestValidator() => RuleFor(request => request.IsEnabled).NotNull()
        .WithMessage("Chọn trạng thái bật/tắt.");
}
