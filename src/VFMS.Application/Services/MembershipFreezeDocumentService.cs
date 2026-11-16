using System.Globalization;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MembershipFreezeDocumentService(
    IMembershipFreezeRequestRepository requests, IMembershipFreezeReviewRepository reviews,
    IMembershipFreezePolicyRepository transactions, IMembershipFreezeDocumentStore documents)
    : IMembershipFreezeDocumentService
{
    public async Task<Result<MembershipFreezeDocumentResponse>> UploadAsync(
        string memberId, int requestId, string fileName, string contentType, long length, Stream content, CancellationToken ct)
    {
        var row = await requests.FindOwnedRequestAsync(memberId, requestId, ct);
        if (row is null)
            return NotFound<MembershipFreezeDocumentResponse>();
        if (length > MembershipFreezeDocumentLimits.MaximumBytes)
            return TooLarge();
        var mediaType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (length <= 0 || mediaType is not ("application/pdf" or "image/png" or "image/jpeg"))
            return Invalid();

        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > MembershipFreezeDocumentLimits.MaximumBytes)
                return TooLarge();
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = output.ToArray();
        if (!MatchesSignature(mediaType, bytes))
            return Invalid();

        return await MutateAsync(memberId, requestId, row.Request.MemberMembershipId, request =>
        {
            var id = Guid.NewGuid();
            var safeName = SafeFileName(fileName, mediaType);
            documents.Set(request, id, safeName, mediaType, bytes);
            return new MembershipFreezeDocumentResponse(id, safeName, mediaType, bytes.LongLength);
        }, ct);
    }

    public async Task<Result<bool>> RemoveAsync(string memberId, int requestId, CancellationToken ct)
    {
        var row = await requests.FindOwnedRequestAsync(memberId, requestId, ct);
        if (row is null)
            return NotFound<bool>();
        return await MutateAsync(memberId, requestId, row.Request.MemberMembershipId, request =>
        {
            documents.Remove(request);
            return true;
        }, ct);
    }

    public async Task<Result<MembershipFreezeDocumentContent>> DownloadAsync(
        string userId, bool isManager, int requestId, CancellationToken ct)
    {
        MembershipFreeze? request;
        if (isManager)
        {
            var row = await reviews.FindAsync(requestId, early: false, ct);
            request = row?.Request.RequestedByUserId == row?.Membership.MemberUserId ? row?.Request : null;
        }
        else
            request = (await requests.FindOwnedRequestAsync(userId, requestId, ct))?.Request;

        if (request?.SupportingDocumentId is not { } id)
            return NotFound<MembershipFreezeDocumentContent>();
        var document = await documents.ReadAsync(id, ct);
        return document is null ? NotFound<MembershipFreezeDocumentContent>()
            : Result<MembershipFreezeDocumentContent>.Success(document);
    }

    private async Task<Result<T>> MutateAsync<T>(
        string memberId, int requestId, int membershipId, Func<MembershipFreeze, T> change, CancellationToken ct)
    {
        var result = NotFound<T>();
        await transactions.ExecuteInSerializableTransactionAsync(async () =>
        {
            // Match UC-15/UC-17 lock order, then recheck ownership and editability before changing the payload.
            var membership = await requests.GetOwnedMembershipAsync(memberId, membershipId, true, ct);
            var request = membership?.MembershipFreezes.SingleOrDefault(
                item => item.Id == requestId && item.RequestedByUserId == memberId);
            if (request is null)
                return;
            if (request.RequestStatus != FreezeRequestStatus.Pending)
            {
                result = Result<T>.Failure("Chỉ yêu cầu đang chờ duyệt mới được thay đổi tài liệu.", "FREEZE_REQUEST_STATE", 409);
                return;
            }

            var data = change(request);
            await requests.SaveChangesAsync(ct);
            result = Result<T>.Success(data);
        }, ct);
        return result;
    }

    private static bool MatchesSignature(string type, byte[] bytes) => type switch
    {
        "application/pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
        "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
        _ => false
    };

    private static string SafeFileName(string fileName, string type)
    {
        var leaf = fileName.Replace('\\', '/').Split('/').Last();
        var safe = new string(leaf.Where(character => !char.IsControl(character) &&
            char.GetUnicodeCategory(character) != UnicodeCategory.Format &&
            !"<>:\"/\\|?*".Contains(character)).ToArray()).Trim(' ', '.');
        var dot = safe.LastIndexOf('.');
        var stem = (dot > 0 ? safe[..dot] : safe).Trim(' ', '.');
        if (stem.Length == 0)
            stem = "document";
        if (stem.Length > 200)
            stem = stem[..200];
        return stem + (type == "application/pdf" ? ".pdf" : type == "image/png" ? ".png" : ".jpg");
    }

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure("Không tìm thấy yêu cầu hoặc tài liệu bảo lưu.", "FREEZE_DOCUMENT_NOT_FOUND", 404);

    private static Result<MembershipFreezeDocumentResponse> Invalid() =>
        Result<MembershipFreezeDocumentResponse>.Failure(
            "Chọn tài liệu PDF, PNG hoặc JPEG có nội dung và định dạng hợp lệ.", "FREEZE_DOCUMENT_INVALID");

    private static Result<MembershipFreezeDocumentResponse> TooLarge() =>
        Result<MembershipFreezeDocumentResponse>.Failure(
            "Tài liệu tối đa 5 MiB.", "FREEZE_DOCUMENT_TOO_LARGE", 413);
}
