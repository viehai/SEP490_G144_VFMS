using FluentValidation;
using VFMS.Contracts.Common;

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
    private const string Prefix = "Từ ngữ không được phép: ";
    private const string LegacyPrefix = "Prohibited words: ";

    public static string Message(IEnumerable<string> words) => Prefix + string.Join(", ", words);

    public static IReadOnlyList<string> ProhibitedWords(string? code, string? message)
    {
        if (code != RejectedCode || message is null) return [];
        var prefix = message.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix : LegacyPrefix;
        return message.StartsWith(prefix, StringComparison.Ordinal)
            ? message[prefix.Length..].Split(", ", StringSplitOptions.RemoveEmptyEntries) : [];
    }
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
        RuleFor(x => x.ApplicationType).Must(x => x is "Recruitment" or "ProfileUpdate").WithMessage("Loại hồ sơ không hợp lệ. Vui lòng kiểm tra lại.");
        // UC 3.3: a ProfileUpdate takes personal details from the account and TrainerProfile, so only Recruitment checks them.
        When(x => x.ApplicationType == "Recruitment", () =>
        {
            RuleFor(x => x.FullName).NotEmpty().WithMessage("Họ tên không được để trống.").MaximumLength(256).WithMessage("Họ tên không được vượt quá {MaxLength} ký tự.");
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email không được để trống.").Must(UserIdentityRules.IsValidEmail).WithMessage("Email không hợp lệ. Vui lòng kiểm tra lại.");
            RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("Số điện thoại không được để trống.").Must(UserIdentityRules.IsValidPhone).WithMessage("Số điện thoại không hợp lệ. Vui lòng kiểm tra lại.");
            RuleFor(x => x.DateOfBirth).Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow) && d.Year >= 1900).WithMessage("Ngày sinh không hợp lệ. Vui lòng kiểm tra lại.");
            RuleFor(x => x.Gender).Must(x => x is "Male" or "Female" or "Other").WithMessage("Giới tính không hợp lệ. Vui lòng kiểm tra lại.");
            RuleFor(x => x.ExperienceYears).InclusiveBetween(0, 80).WithMessage("Số năm kinh nghiệm phải từ {From} đến {To}.");
            RuleFor(x => x.Bio).NotEmpty().WithMessage("Giới thiệu không được để trống.").MaximumLength(4000).WithMessage("Giới thiệu không được vượt quá {MaxLength} ký tự.");
        });
        // UC 3.1: free-text specialization; a ProfileUpdate may leave it empty to keep the published one (UC 3.3).
        RuleFor(x => x.Specialty).NotEmpty().WithMessage("Chuyên môn không được để trống.").When(x => x.ApplicationType == "Recruitment");
        RuleFor(x => x.Specialty).MaximumLength(200).WithMessage("Chuyên môn không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.TrainerRole).Must(TrainerRoleNames.IsValid).WithMessage("Vị trí huấn luyện viên không hợp lệ. Vui lòng kiểm tra lại.").When(x => x.ApplicationType == "Recruitment");
        RuleFor(x => x.Consent).Equal(true).WithMessage("Vui lòng xác nhận trước khi tiếp tục.");
        RuleFor(x => x.Certificates).NotNull().WithMessage("Vui lòng cung cấp danh sách chứng chỉ.");
        RuleFor(x => x).Must(x => (x.Certificates?.Count ?? 0) + (x.CertificateUrls?.Count ?? 0) +
            (x.CertificateLinks?.Count ?? 0) is >= 1 and <= 5).WithMessage("Danh sách chứng chỉ không hợp lệ. Vui lòng kiểm tra lại.")
            .WithName("Certificates");
        RuleFor(x => x.CertificateUrls).Must((request, urls) => urls is null || urls.Count == 0 ||
            (request.ApplicationType == "Recruitment" && urls.Count <= 5 && urls.All(TrainerFileRules.IsCertificateUrl)))
            .WithMessage("Chỉ hồ sơ ứng tuyển mới được dùng liên kết chứng chỉ HTTPS tối đa 2.048 ký tự.");
        RuleForEach(x => x.Certificates).Must(x => TrainerFileRules.ContentType(x) != null)
            .WithMessage("Chứng chỉ phải là tệp PDF/JPEG/PNG đúng định dạng, tối đa 5 MB.");
        RuleForEach(x => x.CertificateLinks).Must(x => TrainerFileRules.IsCertificateUrl(x.Url))
            .WithMessage("Liên kết chứng chỉ phải là địa chỉ HTTPS hợp lệ.");
        RuleFor(x => x.CertificateLinks).Must(links => links == null ||
            links.Select(link => link.ReplacesCertificateId).Where(id => id != null).Distinct().Count() ==
            links.Count(link => link.ReplacesCertificateId != null)).WithMessage("Danh sách liên kết chứng chỉ không hợp lệ. Vui lòng kiểm tra lại.");
    }
}
public sealed class ReviewTrainerApplicationValidator : AbstractValidator<ReviewTrainerApplicationRequest>
{
    public ReviewTrainerApplicationValidator()
    {
        RuleFor(x => x.Action).Must(x => x is "Interview" or "Reject" or "Hire" or "Approve").WithMessage("Quyết định không hợp lệ. Vui lòng kiểm tra lại.");
        RuleFor(x => x.Note).MaximumLength(2000).WithMessage("Ghi chú không được vượt quá {MaxLength} ký tự.");
        // UC 3.2/3.3: interview notes and rejection reasons are mandatory; Hire and Approve notes are optional.
        When(x => x.Action is "Interview" or "Reject", () => RuleFor(x => x.Note).NotEmpty().WithMessage("Ghi chú không được để trống."));
        When(x => x.Action == "Hire", () =>
        {
            RuleFor(x => x.Level).Must(x => x is "Junior" or "Senior" or "Master").WithMessage("Cấp huấn luyện viên không hợp lệ. Vui lòng kiểm tra lại.");
            RuleFor(x => x.IdentityVerified).Equal(true).WithMessage("Vui lòng xác nhận trước khi tiếp tục.");
        });
        RuleFor(x => x.ExistingUserId).MaximumLength(450).WithMessage("Mã tài khoản không được vượt quá {MaxLength} ký tự.");
    }
}
public sealed class TrainerProfileValidator : AbstractValidator<UpdateTrainerProfileRequest>
{
    public TrainerProfileValidator()
    {
        RuleFor(x => x).Must(x => x.Bio != null || x.Avatar != null).WithMessage("Vui lòng cập nhật giới thiệu hoặc ảnh đại diện.");
        RuleFor(x => x.Bio).NotEmpty().WithMessage("Giới thiệu không được để trống.").MaximumLength(4000).WithMessage("Giới thiệu không được vượt quá {MaxLength} ký tự.").When(x => x.Bio != null);
        RuleFor(x => x.Avatar).Must(x => x == null || TrainerFileRules.ContentType(x, true) != null).WithMessage("Ảnh đại diện không hợp lệ. Vui lòng kiểm tra lại.");
    }
}
public sealed class TrainerSetupValidator : AbstractValidator<TrainerSetupRequest>
{
    public TrainerSetupValidator()
    {
        PasswordPolicyRules.ApplyHexToken(RuleFor(x => x.Token), "Mã thiết lập mật khẩu không hợp lệ.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Mật khẩu không được để trống.").Length(8, 32).WithMessage("Mật khẩu phải từ {MinLength} đến {MaxLength} ký tự.").Matches("[A-Z]").WithMessage("Mật khẩu cần ít nhất một chữ hoa.").Matches("[a-z]").WithMessage("Mật khẩu cần ít nhất một chữ thường.").Matches("[0-9]").WithMessage("Mật khẩu cần ít nhất một chữ số.").Matches("[^a-zA-Z0-9]").WithMessage("Mật khẩu cần ít nhất một ký tự đặc biệt.");
    }
}
public sealed class TrainerSearchValidator : AbstractValidator<TrainerSearch>
{
    public TrainerSearchValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10000).WithMessage("Số trang phải từ {From} đến {To}.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("Số mục mỗi trang phải từ {From} đến {To}.");
        RuleFor(x => x.Search).MaximumLength(200).WithMessage("Từ khóa tìm kiếm không được vượt quá {MaxLength} ký tự.");
        RuleFor(x => x.TrainerRole).Must(x => string.IsNullOrEmpty(x) || TrainerRoleNames.IsValid(x)).WithMessage("Vị trí huấn luyện viên không hợp lệ. Vui lòng kiểm tra lại.");
        RuleFor(x => x.Gender).Must(x => x == null || x is "Male" or "Female" or "Other").WithMessage("Giới tính không hợp lệ. Vui lòng kiểm tra lại.");
        RuleFor(x => x.MinimumRating).InclusiveBetween(0, 5).WithMessage("Điểm đánh giá tối thiểu phải từ {From} đến {To}.");
    }
}
public sealed class TrainerApplicationFilterValidator : AbstractValidator<TrainerApplicationFilter>
{
    public TrainerApplicationFilterValidator()
    {
        RuleFor(x => x.ApplicationType).Must(x => x == null || x is "Recruitment" or "ProfileUpdate").WithMessage("Loại hồ sơ không hợp lệ. Vui lòng kiểm tra lại.");
        RuleFor(x => x.Page).InclusiveBetween(1, 10000).WithMessage("Số trang phải từ {From} đến {To}.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("Số mục mỗi trang phải từ {From} đến {To}.");
        RuleFor(x => x.TrainerRole).Must(x => string.IsNullOrEmpty(x) || TrainerRoleNames.IsValid(x)).WithMessage("Vị trí huấn luyện viên không hợp lệ. Vui lòng kiểm tra lại.");
        RuleFor(x => x.Status).Must(x => x == null || x is "Pending" or "Interviewing" or "Hired" or "Rejected" or "UnderReview" or "Approved" or "Blacklisted").WithMessage("Trạng thái không hợp lệ. Vui lòng kiểm tra lại.");
        RuleFor(x => x).Must(x => x.From == null || x.To == null || x.From <= x.To).WithMessage("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
    }
}
