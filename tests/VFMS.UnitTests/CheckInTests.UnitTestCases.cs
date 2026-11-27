using VFMS.Application.Services;
using VFMS.Contracts.CheckIn;
using VFMS.Contracts.CheckIn.Validators;
using VFMS.Domain.Entities;

namespace VFMS.UnitTests;

/// <summary>
/// Report 5.1 unit test cases for the registerFaceId and redeemQrToken sheets, one test per UTCID.
/// Now is 07/11/2026 10:00 gym time (03:00 UTC). Kiosk check-in itself is left out until the dev/viehai rework lands.
/// </summary>
public sealed partial class CheckInTests
{
    // ── FaceProfileService.RegisterAsync + RegisterFaceRequestValidator (UC-43) ──

    [Fact]
    public async Task RegisterFace_UTCID01_FirstRegistrationStoresTheEncryptedTemplate()
    {
        var repo = new RepoFake();

        var result = await new FaceProfileService(repo, new ProtectorFake(), Clock)
            .RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);

        Assert.Equal(201, result.StatusCode);
        Assert.True(result.Data!.IsRegistered);
        Assert.Equal(ProtectorFake.Marker, repo.Face!.FaceNetEmbedding[0]);
        Assert.Equal(Now, repo.Face.ConsentGrantedAt);
        Assert.DoesNotContain("0.1", Assert.Single(repo.Audits).NewValue);
    }

    [Fact]
    public async Task RegisterFace_UTCID02_RegisteringAgainReplacesTheTemplate()
    {
        var repo = new RepoFake();
        var service = new FaceProfileService(repo, new ProtectorFake(), Clock);
        await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);
        var first = repo.Face;

        var result = await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(0.3f), true), null);

        Assert.Equal(200, result.StatusCode);
        Assert.Same(first, repo.Face);
        Assert.Equal(0.3f, new ProtectorFake().Unprotect(repo.Face!.FaceNetEmbedding)[0]);
    }

    [Fact]
    public async Task RegisterFace_UTCID03_RegisteringAfterDeleteReactivatesTheRecord()
    {
        var repo = new RepoFake();
        var service = new FaceProfileService(repo, new ProtectorFake(), Clock);
        await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), true), null);
        await service.DeleteAsync("u1", null);

        var result = await service.RegisterAsync("u1", new RegisterFaceRequest(Descriptor(0.2f), true), null);

        Assert.Equal(200, result.StatusCode);
        Assert.True(repo.Face!.IsActive);
        Assert.False(repo.Face.IsDeleted);
    }

    [Fact]
    public async Task RegisterFace_UTCID04_ServiceRefusesWithoutConsent()
    {
        var repo = new RepoFake();

        var result = await new FaceProfileService(repo, new ProtectorFake(), Clock)
            .RegisterAsync("u1", new RegisterFaceRequest(Descriptor(), false), null);

        Assert.Equal("FACE_CONSENT_REQUIRED", result.ErrorCode);
        Assert.Null(repo.Face);
    }

    [Fact]
    public void RegisterFace_UTCID05_ValidatorRefusesWithoutConsent() =>
        Assert.False(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(Descriptor(), false)).IsValid);

    [Fact]
    public void RegisterFace_UTCID06_ValidatorRefuses127Values() =>
        Assert.False(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(new float[127], true)).IsValid);

    [Fact]
    public void RegisterFace_UTCID07_ValidatorRefuses129Values() =>
        Assert.False(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(new float[129], true)).IsValid);

    [Fact]
    public void RegisterFace_UTCID08_ValidatorAcceptsValuesAtMinusTwoAndTwo()
    {
        var descriptor = Descriptor();
        descriptor[0] = -2f;
        descriptor[1] = 2f;

        Assert.True(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(descriptor, true)).IsValid);
    }

    [Fact]
    public void RegisterFace_UTCID09_ValidatorRefusesAValueAboveTwo()
    {
        var descriptor = Descriptor();
        descriptor[0] = 2.01f;

        Assert.False(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(descriptor, true)).IsValid);
    }

    [Fact]
    public void RegisterFace_UTCID10_ValidatorRefusesNaN()
    {
        var descriptor = Descriptor();
        descriptor[5] = float.NaN;

        Assert.False(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(descriptor, true)).IsValid);
    }

    [Fact]
    public void RegisterFace_UTCID11_ValidatorRefusesAMissingDescriptor() =>
        Assert.False(new RegisterFaceRequestValidator().Validate(new RegisterFaceRequest(null!, true)).IsValid);

    // ── MemberQrService.RedeemAsync (UC-44, UC-42) ───────────────

    private static (RepoFake Repo, MemberQrService Qr) QrCodes(params (string Token, DateTime ExpiresAt, bool Active)[] codes)
    {
        var repo = new RepoFake();
        foreach (var (token, expiresAt, active) in codes)
            repo.Qr.Add(new QRCode { UserId = "u1", QRToken = token, GeneratedAt = Now.AddHours(-1), ExpiresAt = expiresAt, IsActive = active });
        return (repo, new MemberQrService(repo, Clock));
    }

    [Fact]
    public async Task RedeemQr_UTCID01_LiveTokenReturnsItsMember()
    {
        var (_, qr) = QrCodes(("T1", Now.AddHours(5), true));

        Assert.Equal("u1", await qr.RedeemAsync("T1"));
    }

    [Fact]
    public async Task RedeemQr_UTCID02_TokenWithOneSecondLeftStillWorks()
    {
        var (_, qr) = QrCodes(("T1", Now.AddSeconds(1), true));

        Assert.Equal("u1", await qr.RedeemAsync("T1"));
    }

    [Fact]
    public async Task RedeemQr_UTCID03_TokenExpiringExactlyNowIsRefused()
    {
        var (_, qr) = QrCodes(("T1", Now, true));

        Assert.Null(await qr.RedeemAsync("T1"));
    }

    [Fact]
    public async Task RedeemQr_UTCID04_YesterdaysTokenIsRefused()
    {
        var (_, qr) = QrCodes(("T1", Now.AddDays(-1), true));

        Assert.Null(await qr.RedeemAsync("T1"));
    }

    [Fact]
    public async Task RedeemQr_UTCID05_UnknownTokenIsRefused()
    {
        var (_, qr) = QrCodes(("T1", Now.AddHours(5), true));

        Assert.Null(await qr.RedeemAsync("NOT-A-TOKEN"));
    }

    [Fact]
    public async Task RedeemQr_UTCID06_DeactivatedTokenIsRefused()
    {
        var (_, qr) = QrCodes(("T1", Now.AddHours(5), false));

        Assert.Null(await qr.RedeemAsync("T1"));
    }

    [Fact]
    public async Task RedeemQr_UTCID07_FreshlyIssuedTokenLastsUntilTheEndOfTheGymDay()
    {
        var (_, qr) = QrCodes();
        var issued = (await qr.IssueAsync("u2")).Data!;

        Assert.Equal("u2", await qr.RedeemAsync(issued.Token));
        Assert.Equal(new DateTime(2026, 11, 7, 17, 0, 0, DateTimeKind.Utc), issued.ExpiresAt);   // midnight gym time
    }

    [Fact]
    public async Task RedeemQr_UTCID08_OldTokenStopsWorkingWhenANewOneIsIssued()
    {
        var (_, qr) = QrCodes();
        var old = (await qr.IssueAsync("u2")).Data!.Token;
        await qr.IssueAsync("u2");

        Assert.Null(await qr.RedeemAsync(old));
    }
}
