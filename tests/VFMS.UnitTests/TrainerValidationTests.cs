using VFMS.Contracts.Trainers;
using VFMS.Application.Services;
using VFMS.Domain.Entities;

namespace VFMS.UnitTests;

public class TrainerValidationTests
{
    [Fact]
    public void TrainerMayUpdateEitherProfileFieldWithoutResendingTheOther()
    {
        var validator = new TrainerProfileValidator();
        var avatar = new TrainerFileInput("avatar.jpg", [255, 216, 255]);
        Assert.True(validator.Validate(new UpdateTrainerProfileRequest("Updated bio")).IsValid);
        Assert.True(validator.Validate(new UpdateTrainerProfileRequest(Avatar: avatar)).IsValid);
        Assert.True(validator.Validate(new UpdateTrainerProfileRequest("Updated bio", avatar)).IsValid);
        Assert.False(validator.Validate(new UpdateTrainerProfileRequest()).IsValid);
    }

    [Fact]
    public void File_extension_cannot_disguise_other_content()
    {
        Assert.Null(TrainerFileRules.ContentType(new("certificate.pdf", "<script>"u8.ToArray())));
        Assert.Null(TrainerFileRules.ContentType(new("../certificate.pdf", "%PDF-1.7"u8.ToArray())));
        Assert.Null(TrainerFileRules.ContentType(new("avatar.pdf", "%PDF-1.7"u8.ToArray()), true));
        Assert.Equal("application/pdf", TrainerFileRules.ContentType(new("certificate.pdf", "%PDF-1.7"u8.ToArray())));
    }
    [Fact]
    public void Oversized_files_are_rejected_even_with_valid_signature()
    {
        var data = new byte[5 * 1024 * 1024 + 1]; "%PDF-"u8.CopyTo(data);
        Assert.Null(TrainerFileRules.ContentType(new("certificate.pdf", data)));
    }
    [Theory]
    [InlineData("https://certificates.example.test/verify/1", true)]
    [InlineData("http://certificates.example.test/verify/1", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user@certificates.example.test/verify/1", false)]
    public void CertificateLinkRequiresAnAbsoluteHttpsUrlWithoutCredentials(string url, bool valid) =>
        Assert.Equal(valid, TrainerFileRules.IsCertificateUrl(url));

    [Fact]
    public void Hire_requires_valid_level()
    {
        var validator = new ReviewTrainerApplicationValidator();
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Unknown")).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Master")).IsValid);
    }
    [Fact]
    public void Application_requires_consent_and_certificate()
    {
        var r = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000", new DateOnly(2000, 1, 1), "Other", "Strength", 3, "Coach", false, [], TrainerRole: "PT");
        var v = new SubmitTrainerApplicationValidator(); Assert.False(v.Validate(r).IsValid);
        Assert.True(v.Validate(r with { Consent = true, Certificates = [new("proof.pdf", "%PDF-1.7"u8.ToArray())] }).IsValid);
    }
    [Fact]
    public void Profile_update_accepts_new_or_replacement_https_certificate_links()
    {
        var request = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000",
            new(2000, 1, 1), "Other", "PT", 3, "Coach", true, [], "ProfileUpdate",
            CertificateLinks: [new("https://certificates.example.test/new"),
                new("https://certificates.example.test/revised", "old-certificate")]);
        var validator = new SubmitTrainerApplicationValidator();
        Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { CertificateLinks = [new("http://insecure.test/proof")] }).IsValid);
        Assert.False(validator.Validate(request with
        {
            CertificateLinks = [new("https://certificates.example.test/new", "same"),
            new("https://certificates.example.test/revised", "same")]
        }).IsValid);
    }

    // UC 3.1: Recruitment picks the Trainer Role PT or Yoga and a free-text specialization;
    // a ProfileUpdate ignores the Trainer Role and may leave the specialization empty (UC 3.3).
    [Theory]
    [InlineData("Recruitment", "PT", "Strength training", true)]
    [InlineData("Recruitment", "Yoga", "Hatha yoga", true)]
    [InlineData("Recruitment", null, "Strength training", false)]
    [InlineData("Recruitment", "Boxing", "Strength training", false)]
    [InlineData("Recruitment", "PT", "", false)]
    [InlineData("ProfileUpdate", null, "Pilates", true)]
    [InlineData("ProfileUpdate", null, "", true)]
    public void Recruitment_needs_a_trainer_role_and_free_text_specialization(string type, string? role, string specialty, bool valid)
    {
        var request = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000",
            new(2000, 1, 1), "Other", specialty, 3, "Coach", true,
            [new("proof.pdf", "%PDF-1.7"u8.ToArray())], type, TrainerRole: role);
        Assert.Equal(valid, new SubmitTrainerApplicationValidator().Validate(request).IsValid);
        Assert.False(new SubmitTrainerApplicationValidator().Validate(request with { Specialty = new string('x', 201) }).IsValid);
    }

    [Fact]
    public void Banned_word_message_round_trips_only_for_the_content_rejected_code()
    {
        var message = TrainerContentRules.Message(["scam", "spam"]);
        Assert.Equal(["scam", "spam"], TrainerContentRules.ProhibitedWords(TrainerContentRules.RejectedCode, message));
        Assert.Empty(TrainerContentRules.ProhibitedWords("TRAINER_VALIDATION", message));
        Assert.Empty(TrainerContentRules.ProhibitedWords(TrainerContentRules.RejectedCode, "Something else"));
    }

    [Fact]
    public void Application_mapping_keeps_list_fields_and_leaves_detail_fields_empty()
    {
        var config = new Mapster.TypeAdapterConfig();
        config.Scan(typeof(VFMS.Application.Mappings.TrainerMapping).Assembly);
        var application = new VFMS.Domain.Entities.TrainerApplication
        {
            Id = 7,
            TrackingCode = "TR-1",
            FullName = "Applicant",
            Email = "a@example.test",
            PhoneNumber = "0900000000",
            Specialty = "PT",
            InterviewNote = "private",
            ApplicationStatus = VFMS.Domain.Enums.TrainerApplicationStatus.Interviewing,
            CertificatesJson = """[{"Id":"f1","FileName":"proof.pdf","ContentType":"application/pdf"}]"""
        };

        var response = new MapsterMapper.Mapper(config).Map<TrainerApplicationResponse>(application);

        Assert.Equal("Interviewing", response.Status);
        Assert.Equal("TR-1", response.TrackingCode);
        Assert.Equal("Recruitment", response.ApplicationType);
        Assert.Equal("/api/trainers/applications/7/documents/f1", Assert.Single(response.Certificates).DownloadUrl);
        Assert.Null(response.InterviewNote);
        Assert.Null(response.SubmissionHistory);
        Assert.Null(response.AccountMatch);
        Assert.Null(response.TrainerRole);
    }

    [Fact]
    public void Directory_and_application_filters_accept_only_known_trainer_roles()
    {
        Assert.True(new TrainerSearchValidator().Validate(new TrainerSearch(TrainerRole: "Yoga")).IsValid);
        Assert.False(new TrainerSearchValidator().Validate(new TrainerSearch(TrainerRole: "Boxing")).IsValid);
        Assert.True(new TrainerApplicationFilterValidator().Validate(new TrainerApplicationFilter(TrainerRole: "PT")).IsValid);
        Assert.False(new TrainerApplicationFilterValidator().Validate(new TrainerApplicationFilter(TrainerRole: "Boxing")).IsValid);
    }
    [Fact]
    public void Public_list_filters_are_bounded()
    {
        var v = new TrainerSearchValidator(); Assert.False(v.Validate(new TrainerSearch(Page: 0, PageSize: 1000)).IsValid);
        Assert.False(v.Validate(new TrainerSearch(MinimumRating: 6)).IsValid); Assert.True(v.Validate(new TrainerSearch()).IsValid);
    }

    [Theory]
    [InlineData("Recruitment", true)]
    [InlineData("ProfileUpdate", true)]
    [InlineData("CredentialChange", false)]
    [InlineData("", false)]
    public void Application_type_is_explicit_and_profile_update_needs_only_certificates_and_consent(string type, bool valid)
    {
        var request = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000",
            new(2000, 1, 1), "Other", "Strength", 3, "Coach", true, [new("proof.pdf", "%PDF-1.7"u8.ToArray())], type,
            TrainerRole: "PT");
        var validator = new SubmitTrainerApplicationValidator();
        Assert.Equal(valid, validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "ProfileUpdate", Consent = false }).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "ProfileUpdate", Certificates = [] }).IsValid);
        // UC 3.3: personal details come from the account and TrainerProfile, so a ProfileUpdate may omit them.
        Assert.True(validator.Validate(request with
        {
            ApplicationType = "ProfileUpdate",
            PhoneNumber = "",
            FullName = "",
            Bio = "",
            DateOfBirth = default,
            Gender = "",
            Specialty = ""
        }).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "Recruitment", Email = "" }).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "Recruitment", PhoneNumber = "" }).IsValid);
    }

    // UC 3.1 applicant field rules.
    private static SubmitTrainerApplicationRequest Applicant => new("Nguyễn Văn An", "an.nguyen@example.vn", "0912345678",
        new(2000, 1, 1), "Male", "Strength", 3, "Coach", true, [new("proof.pdf", "%PDF-1.7"u8.ToArray())], TrainerRole: "PT");

    private static bool Valid(SubmitTrainerApplicationRequest request) => new SubmitTrainerApplicationValidator().Validate(request).IsValid;

    [Theory]
    [InlineData("Nguyễn Văn An", true)]
    [InlineData("O'Brien-Smith Jr.", true)]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("@@@", false)]
    [InlineData("An 2", false)]
    [InlineData(" An", true)]
    public void Name_has_letters_only(string name, bool valid) => Assert.Equal(valid, Valid(Applicant with { FullName = name }));

    [Fact]
    public void Name_is_at_most_50_characters()
    {
        Assert.True(Valid(Applicant with { FullName = new string('a', 50) }));
        Assert.False(Valid(Applicant with { FullName = new string('a', 51) }));
    }

    [Theory]
    [InlineData("a.b@gmail.com", true)]
    [InlineData("ab@gmail.com", true)]
    [InlineData("an@school.edu.vn", true)]
    [InlineData("an@b-c.com", true)]
    [InlineData("an@localhost", false)]
    [InlineData("an@b.c", false)]
    [InlineData("an@b.123", false)]
    [InlineData("an@-b.com", false)]
    [InlineData("an@b-.com", false)]
    [InlineData("an@b..com", false)]
    [InlineData("an@.com", false)]
    [InlineData("an@b.com.", false)]
    public void Email_domain_needs_a_dot_and_a_letters_only_top_level_domain(string email, bool valid) =>
        Assert.Equal(valid, Valid(Applicant with { Email = email }));

    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("+84912345678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("0912 345 678", "0912345678")]
    [InlineData("0912-345-678", "0912345678")]
    [InlineData("0212345678", null)]
    [InlineData("091234567", null)]
    [InlineData("09123456789", null)]
    [InlineData("(0912) 345-678", null)]
    [InlineData("+1912345678", null)]
    public void Phone_is_a_vietnamese_number_stored_as_a_leading_zero(string phone, string? canonical)
    {
        Assert.Equal(canonical != null, Valid(Applicant with { PhoneNumber = phone }));
        Assert.Equal(canonical != null, TrainerApplicantRules.TryNormalizePhone(phone, out var normalized));
        Assert.Equal(canonical ?? "", normalized);
    }

    [Fact]
    public void Phone_variants_cover_every_spelling_of_the_same_number()
    {
        Assert.Equal(["0912345678", "+84912345678", "84912345678"], TrainerApplicantRules.PhoneVariants("+84 912 345 678"));
        Assert.Equal(["+15551234"], TrainerApplicantRules.PhoneVariants("+1 555 1234"));
    }

    [Fact]
    public void Applicant_must_be_at_least_18_years_old()
    {
        var eighteenToday = TrainerApplicantRules.Today().AddYears(-18);
        Assert.True(Valid(Applicant with { DateOfBirth = eighteenToday, ExperienceYears = 0 }));
        Assert.False(Valid(Applicant with { DateOfBirth = eighteenToday.AddDays(1), ExperienceYears = 0 }));
        Assert.False(Valid(Applicant with { DateOfBirth = new DateOnly(1899, 12, 31) }));
    }

    [Theory]
    [InlineData(30, 0, true)]
    [InlineData(30, 12, true)]
    [InlineData(30, 13, false)]
    [InlineData(30, -1, false)]
    [InlineData(18, 0, true)]
    [InlineData(18, 1, false)]
    public void Experience_cannot_start_before_the_applicant_turned_18(int age, int experience, bool valid) =>
        Assert.Equal(valid, Valid(Applicant with { DateOfBirth = TrainerApplicantRules.Today().AddYears(-age), ExperienceYears = experience }));

    [Fact]
    public void Experience_error_is_reported_on_the_experience_field_for_the_form()
    {
        var result = new SubmitTrainerApplicationValidator().Validate(Applicant with { ExperienceYears = 40 });
        Assert.Equal(["ExperienceYears"], result.Errors.Select(e => e.PropertyName));
    }

    [Fact]
    public void Tracking_code_filter_is_bounded()
    {
        var validator = new TrainerApplicationFilterValidator();
        Assert.True(validator.Validate(new TrainerApplicationFilter(TrackingCode: "TR-abc")).IsValid);
        Assert.False(validator.Validate(new TrainerApplicationFilter(TrackingCode: new string('x', 65))).IsValid);
    }

    [Fact]
    public void Tracking_link_uses_the_configured_page_or_the_reset_site()
    {
        var configured = new VFMS.Application.Settings.TrainerSetupLinkSettings { TrackingPageUrl = "https://gym.example.test/careers/trainer/status" };
        Assert.Equal("https://gym.example.test/careers/trainer/status?code=TR-1", configured.TryBuildTracking("TR-1"));
        var fallback = new VFMS.Application.Settings.TrainerSetupLinkSettings { ResetUrl = "https://gym.example.test/auth/reset?x=1" };
        Assert.Equal("https://gym.example.test/careers/trainer/status?code=TR-1", fallback.TryBuildTracking("TR-1"));
        Assert.Null(new VFMS.Application.Settings.TrainerSetupLinkSettings().TryBuildTracking("TR-1"));
        Assert.Null(new VFMS.Application.Settings.TrainerSetupLinkSettings { TrackingPageUrl = "http://gym.example.test/status" }.TryBuildTracking("TR-1"));
    }

    [Fact]
    public void Only_interview_and_reject_require_a_note()
    {
        var validator = new ReviewTrainerApplicationValidator();
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Approve", "Verified certificates")).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Approve", "")).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Hire", null, "Junior")).IsValid);
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Reject", " ")).IsValid);
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Interview", "")).IsValid);
    }

    [Fact]
    public void Trainer_loses_the_role_only_after_more_than_60_accumulated_not_accepting_days()
    {
        var profile = new TrainerProfile();
        Assert.False(TrainerReadinessRules.HasExpired(profile));
        profile.NotAcceptingAccumulatedDays = 60;
        Assert.False(TrainerReadinessRules.HasExpired(profile));
        profile.NotAcceptingAccumulatedDays = 61;
        Assert.True(TrainerReadinessRules.HasExpired(profile));
        profile.AcceptingNewMembers = true;
        Assert.False(TrainerReadinessRules.HasExpired(profile));
    }
}
