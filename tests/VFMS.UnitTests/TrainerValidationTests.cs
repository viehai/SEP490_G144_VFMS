using VFMS.Contracts.Trainers;
using VFMS.Application.Services;

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
        var r = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000", "000000000001", new DateOnly(2000, 1, 1), "Other", "PT", 3, "Coach", false, []);
        var v = new SubmitTrainerApplicationValidator(); Assert.False(v.Validate(r).IsValid);
        Assert.True(v.Validate(r with { Consent = true, Certificates = [new("proof.pdf", "%PDF-1.7"u8.ToArray())] }).IsValid);
    }
    [Fact]
    public void Profile_update_accepts_new_or_replacement_https_certificate_links()
    {
        var request = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000",
            "000000000001", new(2000, 1, 1), "Other", "PT", 3, "Coach", true, [], "ProfileUpdate",
            CertificateLinks: [new("https://certificates.example.test/new"),
                new("https://certificates.example.test/revised", "old-certificate")]);
        var validator = new SubmitTrainerApplicationValidator();
        Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { CertificateLinks = [new("http://insecure.test/proof")] }).IsValid);
        Assert.False(validator.Validate(request with { CertificateLinks = [new("https://certificates.example.test/new", "same"),
            new("https://certificates.example.test/revised", "same")] }).IsValid);
    }

    [Theory]
    [InlineData("Recruitment", "PT", true)]
    [InlineData("Recruitment", "Yoga", true)]
    [InlineData("Recruitment", "Strength", false)]
    [InlineData("ProfileUpdate", "Yoga", true)]
    [InlineData("ProfileUpdate", "Pilates", false)]
    public void Specialty_is_PT_or_Yoga_for_every_application_type(string type, string specialty, bool valid)
    {
        var request = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000",
            "000000000001", new(2000, 1, 1), "Other", specialty, 3, "Coach", true,
            [new("proof.pdf", "%PDF-1.7"u8.ToArray())], type);
        Assert.Equal(valid, new SubmitTrainerApplicationValidator().Validate(request).IsValid);
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
    public void Directory_and_application_filters_accept_only_known_specialties()
    {
        Assert.True(new TrainerSearchValidator().Validate(new TrainerSearch(Specialty: "Yoga")).IsValid);
        Assert.False(new TrainerSearchValidator().Validate(new TrainerSearch(Specialty: "Boxing")).IsValid);
        Assert.False(new TrainerApplicationFilterValidator().Validate(new TrainerApplicationFilter(Specialty: "Boxing")).IsValid);
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
    public void Application_type_is_explicit_and_profile_update_requires_the_full_application(string type, bool valid)
    {
        var request = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000", "000000000001",
            new(2000, 1, 1), "Other", "PT", 3, "Coach", true, [new("proof.pdf", "%PDF-1.7"u8.ToArray())], type);
        var validator = new SubmitTrainerApplicationValidator();
        Assert.Equal(valid, validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "ProfileUpdate", Consent = false }).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "ProfileUpdate", Certificates = [] }).IsValid);
        Assert.False(validator.Validate(request with { ApplicationType = "ProfileUpdate", CitizenId = "" }).IsValid);
    }

    [Fact]
    public void Profile_approval_requires_a_note_but_not_new_hire_fields()
    {
        var validator = new ReviewTrainerApplicationValidator();
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Approve", "Verified certificates")).IsValid);
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Approve", "")).IsValid);
    }

    [Fact]
    public void Trainer_readiness_changes_the_next_day_and_inactivity_uses_last_contract_end()
    {
        var today = new DateOnly(2026, 11, 15);
        Assert.True(TrainerReadinessRules.IsAccepting(true, false, today.AddDays(1), today));
        Assert.False(TrainerReadinessRules.IsAccepting(true, false, today.AddDays(1), today.AddDays(1)));
        Assert.False(TrainerReadinessRules.IsAccepting(false, true, today.AddDays(1), today));
        Assert.True(TrainerReadinessRules.IsAccepting(false, true, today.AddDays(1), today.AddDays(1)));
        Assert.True(TrainerReadinessRules.HasExpired(new(2026, 8, 1), null, today));
        Assert.False(TrainerReadinessRules.HasExpired(new(2026, 8, 1), new(2026, 10, 1), today));
        Assert.True(TrainerReadinessRules.HasExpired(new(2026, 8, 1), new(2026, 9, 1), today));
    }
}
