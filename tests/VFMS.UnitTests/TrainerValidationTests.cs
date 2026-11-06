using VFMS.Contracts.Trainers;

namespace VFMS.UnitTests;

public class TrainerValidationTests
{
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
    [Fact]
    public void Hire_requires_verified_identity_and_valid_level()
    {
        var validator = new ReviewTrainerApplicationValidator();
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Master", "Tier1", null, false)).IsValid);
        Assert.False(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Unknown", "Tier1", null, true)).IsValid);
        Assert.True(validator.Validate(new ReviewTrainerApplicationRequest("Hire", "Interview completed", "Master", "Tier1", null, true)).IsValid);
    }
    [Fact]
    public void Application_requires_consent_and_certificate()
    {
        var r = new SubmitTrainerApplicationRequest("Test Applicant", "applicant@example.test", "0900000000", "000000000001", new DateOnly(2000, 1, 1), "Other", "Strength", 3, "Coach", false, []);
        var v = new SubmitTrainerApplicationValidator(); Assert.False(v.Validate(r).IsValid);
        Assert.True(v.Validate(r with { Consent = true, Certificates = [new("proof.pdf", "%PDF-1.7"u8.ToArray())] }).IsValid);
    }
    [Fact]
    public void Public_list_filters_are_bounded()
    {
        var v = new TrainerSearchValidator(); Assert.False(v.Validate(new TrainerSearch(Page: 0, PageSize: 1000)).IsValid);
        Assert.False(v.Validate(new TrainerSearch(MinimumRating: 6)).IsValid); Assert.True(v.Validate(new TrainerSearch()).IsValid);
    }
}
