using FluentValidation;

namespace VFMS.Contracts.Trainers;

public static class TrainerFileRules
{
    public static bool IsCertificateUrl(string? value) =>
        value is { Length: > 0 and <= 2048 } && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && uri.UserInfo.Length == 0;

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
/// <summary>Banned-word rejection (GBR-66): the API lists offending words so the client can highlight them (FE guideline 5.5).</summary>
public static class TrainerContentRules
{
    public const string RejectedCode = "TRAINER_CONTENT_REJECTED";
    private const string Prefix = "Prohibited words: ";

    public static string Message(IEnumerable<string> words) => Prefix + string.Join(", ", words);

    public static IReadOnlyList<string> ProhibitedWords(string? code, string? message) =>
        code == RejectedCode && message?.StartsWith(Prefix, StringComparison.Ordinal) == true
            ? message[Prefix.Length..].Split(", ", StringSplitOptions.RemoveEmptyEntries)
            : [];
}

/// <summary>Trainer roles a recruitment can apply for; each maps to a login role (PT -> PT, Yoga -> YogaInstructor).</summary>
public static class TrainerRoleNames
{
    public const string PT = "PT";
    public const string Yoga = "Yoga";
    public static readonly string[] All = [PT, Yoga];
    public static bool IsValid(string? value) => value is PT or Yoga;
}

public sealed class SubmitTrainerApplicationValidator : AbstractValidator<SubmitTrainerApplicationRequest>
{
    public SubmitTrainerApplicationValidator()
    {
        RuleFor(x => x.ApplicationType).Must(x => x is "Recruitment" or "ProfileUpdate");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^\+?[0-9]{9,15}$");
        RuleFor(x => x.CitizenId).NotEmpty().Matches(@"^[0-9]{12}$");
        RuleFor(x => x.DateOfBirth).Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow) && d.Year >= 1900);
        RuleFor(x => x.Gender).Must(x => x is "Male" or "Female" or "Other");
        // UC 3.1: free-text specialization; a ProfileUpdate may leave it empty to keep the published one (UC 3.3).
        RuleFor(x => x.Specialty).NotEmpty().When(x => x.ApplicationType == "Recruitment");
        RuleFor(x => x.Specialty).MaximumLength(200);
        RuleFor(x => x.TrainerRole).Must(TrainerRoleNames.IsValid).When(x => x.ApplicationType == "Recruitment");
        RuleFor(x => x.ExperienceYears).InclusiveBetween(0, 80);
        RuleFor(x => x.Bio).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Consent).Equal(true);
        RuleFor(x => x.Certificates).NotNull();
        RuleFor(x => x).Must(x => (x.Certificates?.Count ?? 0) + (x.CertificateUrls?.Count ?? 0) +
            (x.CertificateLinks?.Count ?? 0) is >= 1 and <= 5)
            .WithName("Certificates");
        RuleFor(x => x.CertificateUrls).Must((request, urls) => urls is null || urls.Count == 0 ||
            (request.ApplicationType == "Recruitment" && urls.Count <= 5 && urls.All(TrainerFileRules.IsCertificateUrl)))
            .WithMessage("Only recruitment accepts HTTPS certificate links of up to 2,048 characters.");
        RuleForEach(x => x.Certificates).Must(x => TrainerFileRules.ContentType(x) != null)
            .WithMessage("Certificates must be PDF/JPEG/PNG with matching file signature and at most 5 MB.");
        RuleForEach(x => x.CertificateLinks).Must(x => TrainerFileRules.IsCertificateUrl(x.Url))
            .WithMessage("Certificate links must use valid HTTPS URLs.");
        RuleFor(x => x.CertificateLinks).Must(links => links == null ||
            links.Select(link => link.ReplacesCertificateId).Where(id => id != null).Distinct().Count() ==
            links.Count(link => link.ReplacesCertificateId != null));
    }
}
public sealed class ReviewTrainerApplicationValidator : AbstractValidator<ReviewTrainerApplicationRequest>
{
    public ReviewTrainerApplicationValidator()
    {
        RuleFor(x => x.Action).Must(x => x is "Interview" or "Reject" or "Hire" or "Approve");
        RuleFor(x => x.Note).NotEmpty().MaximumLength(2000);
        When(x => x.Action == "Hire", () =>
        {
            RuleFor(x => x.Level).Must(x => x is "Junior" or "Senior" or "Master");
            RuleFor(x => x.IdentityVerified).Equal(true);
        });
        RuleFor(x => x.ExistingUserId).MaximumLength(450);
    }
}
public sealed class TrainerProfileValidator : AbstractValidator<UpdateTrainerProfileRequest>
{
    public TrainerProfileValidator()
    {
        RuleFor(x => x).Must(x => x.Bio != null || x.Avatar != null);
        RuleFor(x => x.Bio).NotEmpty().MaximumLength(4000).When(x => x.Bio != null);
        RuleFor(x => x.Avatar).Must(x => x == null || TrainerFileRules.ContentType(x, true) != null);
    }
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
        RuleFor(x => x.Page).InclusiveBetween(1, 10000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.TrainerRole).Must(x => string.IsNullOrEmpty(x) || TrainerRoleNames.IsValid(x));
        RuleFor(x => x.Gender).Must(x => x == null || x is "Male" or "Female" or "Other");
        RuleFor(x => x.MinimumRating).InclusiveBetween(0, 5);
    }
}
public sealed class TrainerApplicationFilterValidator : AbstractValidator<TrainerApplicationFilter>
{
    public TrainerApplicationFilterValidator()
    {
        RuleFor(x => x.ApplicationType).Must(x => x == null || x is "Recruitment" or "ProfileUpdate");
        RuleFor(x => x.Page).InclusiveBetween(1, 10000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.TrainerRole).Must(x => string.IsNullOrEmpty(x) || TrainerRoleNames.IsValid(x));
        RuleFor(x => x.Status).Must(x => x == null || x is "Pending" or "Interviewing" or "Hired" or "Rejected" or "UnderReview" or "Approved" or "Blacklisted");
        RuleFor(x => x).Must(x => x.From == null || x.To == null || x.From <= x.To);
    }
}
