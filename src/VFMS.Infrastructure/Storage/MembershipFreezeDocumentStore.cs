using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Storage;

public sealed class MembershipFreezeDocumentStore(VfmsDbContext context) : IMembershipFreezeDocumentStore
{
    public Task<MembershipFreezeDocumentResponse?> GetMetadataAsync(Guid id, CancellationToken ct) =>
        Metadata([id]).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, MembershipFreezeDocumentResponse>> GetMetadataAsync(Guid[] ids, CancellationToken ct) =>
        await Metadata(ids).ToDictionaryAsync(document => document.DocumentId, ct);

    private IQueryable<MembershipFreezeDocumentResponse> Metadata(Guid[] ids) =>
        context.MembershipFreezes.AsNoTracking().Where(request => request.SupportingDocumentId != null &&
            ids.Contains(request.SupportingDocumentId.Value))
            .Select(request => new MembershipFreezeDocumentResponse(request.SupportingDocumentId!.Value,
                request.SupportingDocumentFileName!, request.SupportingDocumentContentType!,
                request.SupportingDocumentContent!.LongLength));

    public Task<MembershipFreezeDocumentContent?> ReadAsync(Guid id, CancellationToken ct) =>
        context.MembershipFreezes.AsNoTracking().Where(request => request.SupportingDocumentId == id)
            .Select(request => new MembershipFreezeDocumentContent(
                new(id, request.SupportingDocumentFileName!, request.SupportingDocumentContentType!,
                    request.SupportingDocumentContent!.LongLength), request.SupportingDocumentContent!))
            .SingleOrDefaultAsync(ct);

    public void Set(MembershipFreeze request, Guid id, string fileName, string contentType, byte[] content)
    {
        request.SupportingDocumentId = id;
        request.SupportingDocumentFileName = fileName;
        request.SupportingDocumentContentType = contentType;
        request.SupportingDocumentContent = content;
    }

    public void Remove(MembershipFreeze request)
    {
        request.SupportingDocumentId = null;
        request.SupportingDocumentFileName = null;
        request.SupportingDocumentContentType = null;
        request.SupportingDocumentContent = null;
    }
}
