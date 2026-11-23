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
    public void Hire_requires_verified_identity_and_valid_level()
    {
        var validator = new ReviewTrainerApplicationValidator();
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Master", null, false)).IsValid);
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Unknown", null, true)).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Master", IdentityVerified: true)).IsValid);
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

    [Fact]
    public void Only_interview_and_reject_require_a_note()
    {
        var validator = new ReviewTrainerApplicationValidator();
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Approve", "Verified certificates")).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Approve", "")).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Hire", null, "Junior", IdentityVerified: true)).IsValid);
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
