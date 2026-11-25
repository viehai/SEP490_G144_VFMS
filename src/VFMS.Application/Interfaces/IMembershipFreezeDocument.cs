using VFMS.Domain.Entities;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;

namespace VFMS.Application.Interfaces;

public interface IMembershipFreezeDocumentService
{
    Task<Result<MembershipFreezeDocumentResponse>> UploadAsync(
        string memberId, int requestId, string fileName, string contentType, long length, Stream content, CancellationToken ct);
    Task<Result<bool>> RemoveAsync(string memberId, int requestId, CancellationToken ct);
    Task<Result<MembershipFreezeDocumentContent>> DownloadAsync(
        string userId, bool isManager, int requestId, CancellationToken ct);
}

public interface IMembershipFreezeDocumentStore
{
    Task<MembershipFreezeDocumentResponse?> GetMetadataAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, MembershipFreezeDocumentResponse>> GetMetadataAsync(Guid[] ids, CancellationToken ct);
    Task<MembershipFreezeDocumentContent?> ReadAsync(Guid id, CancellationToken ct);
    void Set(MembershipFreeze request, MembershipFreezeDocumentContent content);
}

public sealed record MembershipFreezeDocumentUpload(string FileName, string ContentType, long Length, Stream Content);

public sealed record MembershipFreezeDocumentContent(MembershipFreezeDocumentResponse Document, byte[] Content);
