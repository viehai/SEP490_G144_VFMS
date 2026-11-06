using FluentValidation;

namespace VFMS.Contracts.CheckIn.Validators;

public static class FaceRules
{
    /// <summary>face-api.js FaceRecognitionNet descriptor length.</summary>
    public const int DescriptorLength = 128;
}

public sealed class RegisterFaceRequestValidator : AbstractValidator<RegisterFaceRequest>
{
    public RegisterFaceRequestValidator()
    {
        RuleFor(x => x.ConsentToBiometricProcessing).Equal(true)
            .WithMessage("You must agree to facial biometric processing before registering your face.");
        RuleFor(x => x.Descriptor)
            .NotNull().WithMessage("No face was captured. Please try again.")
            .Must(d => d is { Count: FaceRules.DescriptorLength } && d.All(v => float.IsFinite(v) && v is >= -2f and <= 2f))
            .WithMessage("The face capture is invalid. Please try again.");
    }
}
