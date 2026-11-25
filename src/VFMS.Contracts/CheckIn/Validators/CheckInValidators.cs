using FluentValidation;

namespace VFMS.Contracts.CheckIn.Validators;

public static class FaceRules
{
    /// <summary>face-api.js FaceRecognitionNet descriptor length.</summary>
    public const int DescriptorLength = 128;

    public static bool IsValidDescriptor(IReadOnlyList<float> d) =>
        d is { Count: DescriptorLength } && d.All(v => float.IsFinite(v) && v is >= -2f and <= 2f);
}

public sealed class RegisterFaceRequestValidator : AbstractValidator<RegisterFaceRequest>
{
    public RegisterFaceRequestValidator()
    {
        RuleFor(x => x.ConsentToBiometricProcessing).Equal(true)
            .WithMessage("Bạn cần đồng ý cho xử lý dữ liệu khuôn mặt trước khi đăng ký FaceID.");
        RuleFor(x => x.Descriptor)
            .NotNull().WithMessage("Chưa chụp được khuôn mặt. Vui lòng thử lại.")
            .Must(FaceRules.IsValidDescriptor)
            .WithMessage("Ảnh khuôn mặt không hợp lệ. Vui lòng thử lại.");
    }
}

public sealed class ManualCheckInRequestValidator : AbstractValidator<ManualCheckInRequest>
{
    public ManualCheckInRequestValidator() => RuleFor(x => x.MemberUserId).NotEmpty().WithMessage("Vui lòng chọn hội viên.").MaximumLength(450).WithMessage("Tối đa 450 ký tự.");
}

public sealed class KioskScanRequestValidator : AbstractValidator<KioskScanRequest>
{
    public KioskScanRequestValidator()
    {
        RuleFor(x => x).Must(x => (x.Descriptor is null) != string.IsNullOrWhiteSpace(x.QrToken))
            .WithMessage("Hãy quét khuôn mặt hoặc mã QR.");
        RuleFor(x => x.Descriptor).Must(FaceRules.IsValidDescriptor!).When(x => x.Descriptor is not null)
            .WithMessage("Ảnh khuôn mặt không hợp lệ. Vui lòng thử lại.");
        RuleFor(x => x.QrToken).MaximumLength(128).WithMessage("Tối đa 128 ký tự.");
    }
}
