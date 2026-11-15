using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Storage;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task SupportingDocumentRemainsOptionalForAValidFreezeRequest()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        Assert.Null(request.SupportingDocument);
        using var response = await member.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("application/pdf", "certificate.pdf")]
    [InlineData("image/png", "certificate.png")]
    [InlineData("image/jpeg", "certificate.jpg")]
    public async Task SupportingDocumentUploadIsPrivateAndVisibleToTheOwnerAndReviewingManager(string type, string safeFileName)
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        var bytes = FreezeDocumentBytes(type);
        var document = await ReadFreezeDocumentResponseAsync(await UploadFreezeDocumentAsync(
            member, request.FreezeRequestId, bytes, type, "../../certificate.exe"));
        Assert.NotEqual(Guid.Empty, document.DocumentId);
        Assert.Equal(safeFileName, document.FileName);
        Assert.Equal(type, document.ContentType);
        Assert.Equal(bytes.LongLength, document.Size);
        Assert.Equal(document, (await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).SupportingDocument);
        var review = (await manager.GetFromJsonAsync<MembershipFreezeReviewDetailResponse>(
            $"{FreezeReviewsRoute}/{request.FreezeRequestId}"))!;
        Assert.Equal(document, review.SupportingDocument);

        foreach (var client in new[] { member, manager })
        {
            using var download = await client.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(type, download.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        }

        using var managerUpload = await UploadFreezeDocumentAsync(manager, request.FreezeRequestId, bytes, type);
        using var managerRemove = await manager.DeleteAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(HttpStatusCode.Forbidden, managerUpload.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, managerRemove.StatusCode);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("buyer", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    public async Task FreezeDocumentEndpointsRequireAnAuthorizedRole(string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        using var upload = await UploadFreezeDocumentAsync(client, 1, FreezeDocumentBytes("application/pdf"), "application/pdf");
        using var remove = await client.DeleteAsync(FreezeDocumentRoute(1));
        using var download = await client.GetAsync(FreezeDocumentRoute(1));
        Assert.All(new[] { upload, remove, download }, response => Assert.Equal(expected, response.StatusCode));
    }

    [Fact]
    public async Task AnotherMemberCannotUploadReplaceRemoveOrDownloadAnOwnedFreezeDocument()
    {
        var membership = await PrepareFreezeMembershipAsync();
        await SeedMembershipAsync(Today.AddDays(20), VFMS.Domain.Enums.MembershipStatus.Active, "buyer", "Customer");
        using var member = Client("member", "Member");
        using var other = Client("buyer", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        var bytes = FreezeDocumentBytes("application/pdf");
        var original = await ReadFreezeDocumentResponseAsync(await UploadFreezeDocumentAsync(member, request.FreezeRequestId, bytes, "application/pdf"));
        using var upload = await UploadFreezeDocumentAsync(other, request.FreezeRequestId, bytes, "application/pdf");
        using var remove = await other.DeleteAsync(FreezeDocumentRoute(request.FreezeRequestId));
        using var download = await other.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.All(new[] { upload, remove, download }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(original, (await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).SupportingDocument);
    }

    [Theory]
    [InlineData("application/octet-stream", "application/pdf")]
    [InlineData("application/pdf", "image/png")]
    [InlineData("image/jpeg", "application/pdf")]
    public async Task DocumentUploadRejectsUnsupportedTypesAndMismatchedSignatures(string claimedType, string actualType)
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        using var response = await UploadFreezeDocumentAsync(member, request.FreezeRequestId, FreezeDocumentBytes(actualType), claimedType);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).SupportingDocument);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(MembershipFreezeDocumentLimits.MaximumBytes, HttpStatusCode.OK)]
    [InlineData(MembershipFreezeDocumentLimits.MaximumBytes + 1, HttpStatusCode.RequestEntityTooLarge)]
    public async Task DocumentUploadEnforcesTheOptionalFilesSizeBoundary(int length, HttpStatusCode expected)
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        var bytes = new byte[length];
        if (length > 0)
            Encoding.ASCII.GetBytes("%PDF-").CopyTo(bytes, 0);
        using var response = await UploadFreezeDocumentAsync(member, request.FreezeRequestId, bytes, "application/pdf");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task PendingDocumentCanBeReplacedAndRemovedWithoutLeavingStoredPayloads()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        var first = await ReadFreezeDocumentResponseAsync(await UploadFreezeDocumentAsync(member,
            request.FreezeRequestId, FreezeDocumentBytes("application/pdf"), "application/pdf"));
        var replacementBytes = FreezeDocumentBytes("image/png");
        var replacement = await ReadFreezeDocumentResponseAsync(await UploadFreezeDocumentAsync(member,
            request.FreezeRequestId, replacementBytes, "image/png"));
        Assert.NotEqual(first.DocumentId, replacement.DocumentId);
        using var download = await member.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(replacementBytes, await download.Content.ReadAsByteArrayAsync());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(replacement.DocumentId, (await db.MembershipFreezes.AsNoTracking().SingleAsync()).SupportingDocumentId);

        using var remove = await member.DeleteAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.True(await remove.Content.ReadFromJsonAsync<bool>());
        Assert.Null((await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).SupportingDocument);
        var cleared = await db.MembershipFreezes.AsNoTracking().SingleAsync();
        Assert.Null(cleared.SupportingDocumentId);
        Assert.Null(cleared.SupportingDocumentFileName);
        Assert.Null(cleared.SupportingDocumentContentType);
        Assert.Null(cleared.SupportingDocumentContent);
        using var missing = await member.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ApprovedFreezeDocumentRemainsReadableButCannotBeChangedByTheMember()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        var bytes = FreezeDocumentBytes("application/pdf");
        var document = await ReadFreezeDocumentResponseAsync(await UploadFreezeDocumentAsync(member,
            request.FreezeRequestId, bytes, "application/pdf"));
        var approved = await ApproveFreezeReviewAsync(manager, request.FreezeRequestId);
        Assert.Equal(document, approved.SupportingDocument);
        using var upload = await UploadFreezeDocumentAsync(member, request.FreezeRequestId, bytes, "application/pdf");
        using var remove = await member.DeleteAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
        using var read = await manager.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(bytes, await read.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task FailedReplacementRollsBackTheReferenceAndPreservesTheOriginalPrivateDocument()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        var bytes = FreezeDocumentBytes("application/pdf");
        var original = await ReadFreezeDocumentResponseAsync(await UploadFreezeDocumentAsync(member,
            request.FreezeRequestId, bytes, "application/pdf"));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_freeze_document_update BEFORE UPDATE OF SupportingDocumentContent ON MembershipFreezes
            BEGIN
                SELECT RAISE(ABORT, 'test document replacement failure');
            END;
            """);
        using var failed = await UploadFreezeDocumentAsync(member, request.FreezeRequestId, FreezeDocumentBytes("image/png"), "image/png");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(original, (await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).SupportingDocument);
        using var download = await member.GetAsync(FreezeDocumentRoute(request.FreezeRequestId));
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(original.DocumentId, (await db.MembershipFreezes.AsNoTracking().SingleAsync()).SupportingDocumentId);
    }

    [Fact]
    public async Task DocumentUploadRacingApprovalCannotMutateAFinalizedRequest()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        await WithFreezeLifecycleDatabaseAsync(async connectionString =>
        {
            using var start = new Barrier(2);
            var upload = Task.Run(async () =>
            {
                await using var db = FileFreezeContext(connectionString);
                var documents = new MembershipFreezeDocumentService(new MembershipFreezeRequestRepository(db),
                    new MembershipFreezeReviewRepository(db), new MembershipFreezePolicyRepository(db), new MembershipFreezeDocumentStore(db));
                using var content = new MemoryStream(FreezeDocumentBytes("application/pdf"));
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Document upload did not reach the review start barrier.");
                return await documents.UploadAsync("member", request.FreezeRequestId, "certificate.pdf", "application/pdf",
                    content.Length, content, CancellationToken.None);
            });
            var approval = RunLifecycleManagerDecisionAsync(connectionString, request.FreezeRequestId, early: false, start);
            await Task.WhenAll(upload, approval);
            var approvalResult = await approval;
            var uploadResult = await upload;
            Assert.True(approvalResult.IsSuccess, approvalResult.ErrorMessage);
            Assert.True(uploadResult.IsSuccess || uploadResult.StatusCode == 409, uploadResult.ErrorMessage);

            await using var verification = FileFreezeContext(connectionString);
            var freeze = await verification.MembershipFreezes.AsNoTracking().SingleAsync();
            Assert.Equal(VFMS.Domain.Enums.FreezeRequestStatus.Approved, freeze.RequestStatus);
            Assert.Equal(uploadResult.IsSuccess ? uploadResult.Data!.DocumentId : (Guid?)null, freeze.SupportingDocumentId);
            Assert.Equal(uploadResult.IsSuccess ? 1 : 0, await verification.MembershipFreezes.CountAsync(item => item.SupportingDocumentId != null));
        });
    }

    private static string FreezeDocumentRoute(int requestId) => $"{FreezeRequestsRoute}/{requestId}/document";

    private static byte[] FreezeDocumentBytes(string type) => type switch
    {
        "application/pdf" => "%PDF-1.4\nfixture"u8.ToArray(),
        "image/png" => [137, 80, 78, 71, 13, 10, 26, 10, 0],
        "image/jpeg" => [255, 216, 255, 224],
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static async Task<HttpResponseMessage> UploadFreezeDocumentAsync(
        HttpClient member, int requestId, byte[] bytes, string type, string name = "certificate.pdf")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        return await member.PostAsync(FreezeDocumentRoute(requestId), form);
    }

    private static async Task<MembershipFreezeDocumentResponse> ReadFreezeDocumentResponseAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK, got {response.StatusCode}. {body}");
            return JsonSerializer.Deserialize<MembershipFreezeDocumentResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }
}
