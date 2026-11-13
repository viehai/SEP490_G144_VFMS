using System.Text;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed partial class MembershipFreezeRequestTests
{
    [Fact]
    public async Task SupportingDocumentUsesSafeMetadataAndTheExistingParentTransaction()
    {
        var fixture = new FreezeFixture();
        var request = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);
        var store = new DocumentStoreStub();
        var service = DocumentService(fixture, store);
        using var cancellation = new CancellationTokenSource();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\ncertificate");
        using var content = new MemoryStream(bytes);

        var result = await service.UploadAsync(MemberId, request.Id, "C:\\private\\medical\r\n\u202Ereport.html",
            "application/pdf", bytes.Length, content, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("medicalreport.pdf", result.Data!.FileName);
        Assert.Equal("application/pdf", result.Data.ContentType);
        Assert.Equal(request.SupportingDocumentId, result.Data.DocumentId);
        Assert.Equal(bytes, Assert.Single(store.Documents).Value.Content);
        Assert.Equal(cancellation.Token, fixture.Policies.LastToken);
        Assert.Equal(cancellation.Token, fixture.Repository.LastSaveToken);
        Assert.Equal(FreezeRequestStatus.Pending, request.RequestStatus);
        Assert.Equal(0, fixture.Membership.TotalFrozenDays);
        Assert.Equal(MembershipStatus.Active, fixture.Membership.MembershipStatus);
    }

    [Fact]
    public async Task ReplacingAndRemovingDocumentsLeavesNoUnusedPayloads()
    {
        var fixture = new FreezeFixture();
        var request = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);
        var store = new DocumentStoreStub();
        var service = DocumentService(fixture, store);
        using var first = new MemoryStream("%PDF-first"u8.ToArray());
        var original = await service.UploadAsync(MemberId, request.Id, "first.pdf", "application/pdf", first.Length, first, default);
        using var second = new MemoryStream("%PDF-second"u8.ToArray());
        var replaced = await service.UploadAsync(MemberId, request.Id, "second.pdf", "application/pdf", second.Length, second, default);

        Assert.NotEqual(original.Data!.DocumentId, replaced.Data!.DocumentId);
        Assert.Equal(replaced.Data.DocumentId, Assert.Single(store.Documents).Key);
        Assert.Equal(replaced.Data.DocumentId, request.SupportingDocumentId);
        Assert.True((await service.RemoveAsync(MemberId, request.Id, default)).Data);
        Assert.Null(request.SupportingDocumentId);
        Assert.Empty(store.Documents);
    }

    [Theory]
    [InlineData(FreezeRequestStatus.Approved)]
    [InlineData(FreezeRequestStatus.Active)]
    [InlineData(FreezeRequestStatus.Rejected)]
    [InlineData(FreezeRequestStatus.Cancelled)]
    [InlineData(FreezeRequestStatus.Expired)]
    [InlineData(FreezeRequestStatus.Completed)]
    public async Task FinalizedDocumentsRemainReadableAndCannotBeReplacedOrRemoved(FreezeRequestStatus status)
    {
        var fixture = new FreezeFixture();
        var request = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);
        var store = new DocumentStoreStub();
        var service = DocumentService(fixture, store);
        using var first = new MemoryStream("%PDF-first"u8.ToArray());
        var original = await service.UploadAsync(MemberId, request.Id, "first.pdf", "application/pdf", first.Length, first, default);
        request.RequestStatus = status;
        using var second = new MemoryStream("%PDF-second"u8.ToArray());

        Assert.Equal(409, (await service.UploadAsync(MemberId, request.Id, "second.pdf", "application/pdf",
            second.Length, second, default)).StatusCode);
        Assert.Equal(409, (await service.RemoveAsync(MemberId, request.Id, default)).StatusCode);
        Assert.Equal(original.Data!.DocumentId, request.SupportingDocumentId);
        Assert.Equal(original.Data.DocumentId, Assert.Single(store.Documents).Key);
        Assert.True((await service.DownloadAsync(MemberId, false, request.Id, default)).IsSuccess);
        Assert.True((await service.DownloadAsync("manager", true, request.Id, default)).IsSuccess);
        Assert.Equal(404, (await service.DownloadAsync(OtherMemberId, false, request.Id, default)).StatusCode);
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task ActualStreamSizeIsBoundedEvenWhenReportedLengthIsSmaller()
    {
        var fixture = new FreezeFixture();
        var request = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(3), 3);
        var store = new DocumentStoreStub();
        using var content = new MemoryStream(new byte[MembershipFreezeDocumentLimits.MaximumBytes + 1]);

        var result = await DocumentService(fixture, store).UploadAsync(
            MemberId, request.Id, "file.pdf", "application/pdf", 1, content, default);

        Assert.Equal(413, result.StatusCode);
        Assert.Empty(store.Documents);
        Assert.Null(request.SupportingDocumentId);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task MemberListBatchesDocumentMetadataWithoutReadingPayloads()
    {
        var fixture = new FreezeFixture();
        var store = new DocumentStoreStub();
        foreach (var offset in new[] { 3, 10 })
        {
            var request = fixture.AddRequest(FreezeRequestStatus.Pending, Today.AddDays(offset), 3);
            request.SupportingDocumentId = Guid.NewGuid();
            store.Set(request, request.SupportingDocumentId.Value, "certificate.pdf", "application/pdf", "%PDF-data"u8.ToArray());
        }
        var service = new MembershipFreezeRequestService(fixture.Repository, fixture.Policies,
            new MembershipFreezeRequestValidator(), new TestClock(), store);

        var result = await service.ListAsync(MemberId, default);

        Assert.All(result.Data!, request => Assert.NotNull(request.SupportingDocument));
        Assert.Equal(1, store.BatchMetadataReads);
        Assert.Equal(0, store.SingleMetadataReads);
        Assert.Equal(0, store.PayloadReads);
    }

    private static MembershipFreezeDocumentService DocumentService(FreezeFixture fixture, DocumentStoreStub store) =>
        new(fixture.Repository, new DocumentReviewRepository(fixture.Membership), fixture.Policies, store);

    private sealed class DocumentStoreStub : IMembershipFreezeDocumentStore
    {
        public Dictionary<Guid, MembershipFreezeDocumentContent> Documents { get; } = [];
        public int BatchMetadataReads { get; private set; }
        public int SingleMetadataReads { get; private set; }
        public int PayloadReads { get; private set; }

        public Task<MembershipFreezeDocumentResponse?> GetMetadataAsync(Guid id, CancellationToken ct)
        {
            SingleMetadataReads++;
            return Task.FromResult(Documents.GetValueOrDefault(id)?.Document);
        }
        public Task<IReadOnlyDictionary<Guid, MembershipFreezeDocumentResponse>> GetMetadataAsync(Guid[] ids, CancellationToken ct)
        {
            BatchMetadataReads++;
            return Task.FromResult<IReadOnlyDictionary<Guid, MembershipFreezeDocumentResponse>>(
                Documents.Where(item => ids.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value.Document));
        }
        public Task<MembershipFreezeDocumentContent?> ReadAsync(Guid id, CancellationToken ct)
        {
            PayloadReads++;
            return Task.FromResult(Documents.GetValueOrDefault(id));
        }
        public void Set(MembershipFreeze request, Guid id, string fileName, string contentType, byte[] content)
        {
            Remove(request);
            request.SupportingDocumentId = id;
            Documents.Add(id, new(new(id, fileName, contentType, content.LongLength), content));
        }
        public void Remove(MembershipFreeze request)
        {
            if (request.SupportingDocumentId is { } id)
                Documents.Remove(id);
            request.SupportingDocumentId = null;
        }
    }

    private sealed class DocumentReviewRepository(MemberMembership membership) : IMembershipFreezeReviewRepository
    {
        public Task<MembershipFreezeReviewReadModel?> FindAsync(int requestId, bool early, CancellationToken ct)
        {
            var request = membership.MembershipFreezes.SingleOrDefault(item => item.Id == requestId);
            return Task.FromResult(request is null ? null : new MembershipFreezeReviewReadModel(
                request, "Member", "member@example.test", membership.OrderItem.PackageNameSnapshot, membership));
        }
        public Task<(IReadOnlyList<MembershipFreezeReviewReadModel> Items, int TotalCount)> SearchAsync(
            string? type, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
