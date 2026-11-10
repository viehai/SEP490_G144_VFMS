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
            .WithMessage("You must agree to facial biometric processing before registering your face.");
        RuleFor(x => x.Descriptor)
            .NotNull().WithMessage("No face was captured. Please try again.")
            .Must(FaceRules.IsValidDescriptor)
            .WithMessage("The face capture is invalid. Please try again.");
    }
}

public sealed class KioskScanRequestValidator : AbstractValidator<KioskScanRequest>
{
    public KioskScanRequestValidator()
    {
        RuleFor(x => x).Must(x => (x.Descriptor is null) != string.IsNullOrWhiteSpace(x.QrToken))
            .WithMessage("Scan either a face or a QR code.");
        RuleFor(x => x.Descriptor).Must(FaceRules.IsValidDescriptor!).When(x => x.Descriptor is not null)
            .WithMessage("The face capture is invalid. Please try again.");
        RuleFor(x => x.QrToken).MaximumLength(128);
    }
}
