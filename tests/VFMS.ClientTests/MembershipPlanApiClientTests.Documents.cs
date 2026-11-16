using System.Net;
using System.Net.Http.Headers;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Fact]
    public async Task UploadFreezeDocument_SendsAuthenticatedMultipartAndReadsSafeMetadata()
    {
        var bytes = "%PDF-certificate"u8.ToArray();
        var expected = new MembershipFreezeDocumentResponse(Guid.NewGuid(), "certificate.pdf", "application/pdf", bytes.Length);
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-freeze-requests/9/document", request.RequestUri!.PathAndQuery);
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var file = Assert.Single(multipart);
            Assert.Equal("file", file.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("certificate.pdf", file.Headers.ContentDisposition.FileName!.Trim('"'));
            Assert.Equal("application/pdf", file.Headers.ContentType!.MediaType);
            Assert.Equal(bytes, await file.ReadAsByteArrayAsync());
            return Json(expected);
        }, role: "Member");
        using var content = new MemoryStream(bytes);

        Assert.Equal(expected, await api.UploadFreezeDocumentAsync(9, content, "certificate.pdf", "application/pdf"));
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("Manager")]
    public async Task DownloadFreezeDocument_UsesBearerAndReadsBinaryContent(string role)
    {
        byte[] expected = [137, 80, 78, 71, 13, 10, 26, 10, 0, 255];
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-freeze-requests/9/document", request.RequestUri!.PathAndQuery);
            var content = new ByteArrayContent(expected);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "certificate.png" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }, role: role);

        var result = await api.DownloadFreezeDocumentAsync(9);
        Assert.Equal(expected, result.Content);
        Assert.Equal("certificate.png", result.FileName);
        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public async Task RemoveFreezeDocument_UsesBodylessDelete()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-freeze-requests/9/document", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(true));
        }, role: "Member");

        Assert.True(await api.RemoveFreezeDocumentAsync(9));
    }

    [Fact]
    public async Task BinaryDownload_PreservesStandardAuthorizationErrors()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(
            new { code = "FREEZE_DOCUMENT_NOT_FOUND", detail = "Document not found." }, HttpStatusCode.NotFound)), role: "Member");

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.DownloadFreezeDocumentAsync(9));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("FREEZE_DOCUMENT_NOT_FOUND", error.ErrorCode);
        Assert.Equal("Document not found.", error.Message);
    }

    [Fact]
    public async Task MemberTracking_ReadsSupportingDocumentMetadata()
    {
        var metadata = new MembershipFreezeDocumentResponse(Guid.NewGuid(), "medical.pdf", "application/pdf", 100);
        var expected = PendingFreezeRequest() with { SupportingDocument = metadata };
        using var api = CreateClient(_ => Task.FromResult(Json(new[] { expected })), role: "Member");

        Assert.Equal(metadata, Assert.Single(await api.GetMyFreezeRequestsAsync()).SupportingDocument);
    }

    [Fact]
    public async Task ManagerReview_ReadsSupportingDocumentMetadata()
    {
        var metadata = new MembershipFreezeDocumentResponse(Guid.NewGuid(), "medical.pdf", "application/pdf", 100);
        var expected = FreezeReviewDetail() with { SupportingDocument = metadata };
        using var api = CreateClient(_ => Task.FromResult(Json(expected)));

        Assert.Equal(metadata, (await api.GetFreezeReviewAsync(9, MembershipFreezeReviewTypes.Freeze)).SupportingDocument);
    }
}
