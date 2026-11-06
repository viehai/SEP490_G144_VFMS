using FluentValidation;

namespace VFMS.Contracts.Trainers;

public static class TrainerFileRules
{
    public static string? ContentType(TrainerFileInput? f, bool avatar = false)
    {
        if (f?.Content is not { Length: > 0 and <= 5 * 1024 * 1024 } || string.IsNullOrWhiteSpace(f.FileName)
            || f.FileName.Length > 200 || f.FileName.IndexOfAny(['/', '\\', '\r', '\n']) >= 0) return null;
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        var b = f.Content.AsSpan();
        if (!avatar && ext == ".pdf" && b.StartsWith("%PDF-"u8)) return "application/pdf";
        if (ext is ".jpg" or ".jpeg" && b.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (ext == ".png" && b.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        return null;
    }
}
public sealed class SubmitTrainerApplicationValidator : AbstractValidator<SubmitTrainerApplicationRequest>
{
    public SubmitTrainerApplicationValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^\+?[0-9]{9,15}$");
        RuleFor(x => x.CitizenId).NotEmpty().Matches(@"^[0-9]{12}$");
        RuleFor(x => x.DateOfBirth).Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow) && d.Year >= 1900);
        RuleFor(x => x.Gender).Must(x => x is "Male" or "Female" or "Other");
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExperienceYears).InclusiveBetween(0, 80);
        RuleFor(x => x.Bio).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Consent).Equal(true);
        RuleFor(x => x.Certificates).NotNull().Must(x => x is { Count: >= 1 and <= 5 });
        RuleForEach(x => x.Certificates).Must(x => TrainerFileRules.ContentType(x) != null)
            .WithMessage("Certificates must be PDF/JPEG/PNG with matching file signature and at most 5 MB.");
    }
}
public sealed class ReviewTrainerApplicationValidator : AbstractValidator<ReviewTrainerApplicationRequest>
{
    public ReviewTrainerApplicationValidator()
    {
        RuleFor(x => x.Action).Must(x => x is "Interview" or "Reject" or "Hire");
        RuleFor(x => x.Note).NotEmpty().MaximumLength(2000);
        When(x => x.Action == "Hire", () =>
        {
            RuleFor(x => x.Level).Must(x => x is "Junior" or "Senior" or "Master");
            RuleFor(x => x.CommissionTier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.IdentityVerified).Equal(true);
        });
        RuleFor(x => x.ExistingUserId).MaximumLength(450);
    }
}
public sealed class RestrictionValidator : AbstractValidator<RecruitmentRestrictionRequest>
{
    public RestrictionValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.CitizenId).NotEmpty().Matches(@"^[0-9]{12}$");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}
public sealed class TrainerProfileValidator : AbstractValidator<UpdateTrainerProfileRequest>
{
    public TrainerProfileValidator()
    {
        RuleFor(x => x.Bio).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Avatar).Must(x => x == null || TrainerFileRules.ContentType(x, true) != null);
    }
}
public sealed class CredentialValidator : AbstractValidator<TrainerCredentialRequest>
{
    public CredentialValidator()
    {
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Certificates).NotNull().Must(x => x is { Count: >= 1 and <= 5 });
        RuleForEach(x => x.Certificates).Must(x => TrainerFileRules.ContentType(x) != null);
    }
}
public sealed class CredentialReviewValidator : AbstractValidator<ReviewTrainerCredentialRequest>
{
    public CredentialReviewValidator() { RuleFor(x => x.Note).NotEmpty().MaximumLength(2000); }
}
public sealed class TrainerSetupValidator : AbstractValidator<TrainerSetupRequest>
{
    public TrainerSetupValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().Length(8, 32).Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]").Matches("[^a-zA-Z0-9]");
    }
}
public sealed class TrainerSearchValidator : AbstractValidator<TrainerSearch>
{
    public TrainerSearchValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200); RuleFor(x => x.Specialty).MaximumLength(200);
        RuleFor(x => x.Gender).Must(x => x == null || x is "Male" or "Female" or "Other");
        RuleFor(x => x.MinimumRating).InclusiveBetween(0, 5);
    }
}
public sealed class TrainerApplicationFilterValidator : AbstractValidator<TrainerApplicationFilter>
{
    public TrainerApplicationFilterValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Specialty).MaximumLength(200);
        RuleFor(x => x.Status).Must(x => x == null || x is "Pending" or "Interviewing" or "Hired" or "Rejected" or "UnderReview" or "Approved" or "Blacklisted");
        RuleFor(x => x).Must(x => x.From == null || x.To == null || x.From <= x.To);
    }
}
