using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;

namespace VFMS.Client.Services;

public sealed class MembershipPlanApiClient(HttpClient http, AuthSession session) : IDisposable
{
    private const string BasePath = "api/manager/membership-plans";
    private const string PurchasesPath = "api/membership-purchases";
    private const string RenewalsPath = "api/membership-renewals";
    private const string MembershipsPath = "api/memberships";
    private const string FreezePolicyPath = "api/manager/membership-freeze-policy";
    private const string FreezeRequestsPath = "api/membership-freeze-requests";
    private const string FreezeReviewsPath = "api/manager/membership-freeze-requests";

    public Task<PagedResponse<MembershipPlanResponse>> ListAsync(MembershipPlanListRequest request, CancellationToken cancellationToken = default)
    {
        var path = FormattableString.Invariant($"{BasePath}?page={request.Page}&pageSize={request.PageSize}");
        if (!string.IsNullOrWhiteSpace(request.Search))
            path += $"&search={Uri.EscapeDataString(request.Search.Trim())}";
        if (!string.IsNullOrWhiteSpace(request.Status))
            path += $"&status={Uri.EscapeDataString(request.Status)}";
        return SendAsync<PagedResponse<MembershipPlanResponse>>(HttpMethod.Get, path, cancellationToken);
    }

    public Task<MembershipPlanResponse> GetAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPlanResponse>(HttpMethod.Get, $"{BasePath}/{id}", cancellationToken);

    public Task<MembershipPlanResponse> CreateAsync(MembershipPlanRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPlanResponse>(HttpMethod.Post, BasePath, cancellationToken, JsonContent.Create(request));

    public Task<MembershipPlanResponse> UpdateAsync(int id, MembershipPlanRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPlanResponse>(HttpMethod.Put, $"{BasePath}/{id}", cancellationToken, JsonContent.Create(request));

    public Task<MembershipPlanResponse> ChangeStatusAsync(int id, string status, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPlanResponse>(HttpMethod.Patch, $"{BasePath}/{id}/status", cancellationToken,
            JsonContent.Create(new MembershipPlanStatusRequest { Status = status }));

    public Task<MembershipPlanDeleteResponse> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPlanDeleteResponse>(HttpMethod.Delete, $"{BasePath}/{id}", cancellationToken);

    public Task<MembershipFreezePolicyResponse> GetMembershipFreezePolicyAsync(CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezePolicyResponse>(HttpMethod.Get, FreezePolicyPath, cancellationToken);

    public Task<MembershipFreezePolicyResponse> UpdateMembershipFreezePolicyAsync(
        MembershipFreezePolicyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezePolicyResponse>(HttpMethod.Put, FreezePolicyPath, cancellationToken, JsonContent.Create(request));

    public Task<PagedResponse<MembershipPlanCatalogItemResponse>> ListCatalogAsync(int page, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<MembershipPlanCatalogItemResponse>>(HttpMethod.Get,
            FormattableString.Invariant($"api/membership-plans?page={page}&pageSize=9"), cancellationToken);

    public Task<MembershipPurchaseResponse> PurchaseAsync(MembershipPurchaseRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post, PurchasesPath, cancellationToken, JsonContent.Create(request));

    public Task<MembershipPurchaseResponse> GetPurchaseAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Get, $"{PurchasesPath}/{Uri.EscapeDataString(orderNumber)}", cancellationToken);

    public Task<IReadOnlyList<MembershipSummaryResponse>> GetMyMembershipsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<MembershipSummaryResponse>>(HttpMethod.Get, $"{MembershipsPath}/me", cancellationToken);

    public Task<MembershipDetailResponse> GetMembershipDetailAsync(int membershipId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipDetailResponse>(HttpMethod.Get,
            FormattableString.Invariant($"{MembershipsPath}/{membershipId}"), cancellationToken);

    public Task<MembershipFreezeEligibilityResponse> GetFreezeEligibilityAsync(
        int membershipId, int? excludeRequestId = null, CancellationToken cancellationToken = default)
    {
        var path = FormattableString.Invariant($"{MembershipsPath}/{membershipId}/freeze-eligibility");
        if (excludeRequestId is { } requestId)
            path += FormattableString.Invariant($"?excludeRequestId={requestId}");
        return SendAsync<MembershipFreezeEligibilityResponse>(HttpMethod.Get, path, cancellationToken);
    }

    public Task<IReadOnlyList<MembershipFreezeRequestResponse>> GetMyFreezeRequestsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<MembershipFreezeRequestResponse>>(HttpMethod.Get, FreezeRequestsPath, cancellationToken);

    public Task<MembershipFreezeRequestResponse> GetFreezeRequestAsync(int requestId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeRequestResponse>(HttpMethod.Get,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}"), cancellationToken);

    public Task<MembershipFreezeRequestResponse> CreateFreezeRequestAsync(
        MembershipFreezeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeRequestResponse>(HttpMethod.Post, FreezeRequestsPath, cancellationToken, JsonContent.Create(request));

    public Task<MembershipFreezeRequestResponse> UpdateFreezeRequestAsync(
        int requestId, MembershipFreezeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeRequestResponse>(HttpMethod.Put,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}"), cancellationToken, JsonContent.Create(request));

    public Task<MembershipFreezeRequestResponse> CancelFreezeRequestAsync(int requestId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeRequestResponse>(HttpMethod.Post,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}/cancel"), cancellationToken);

    public Task<MembershipFreezeRequestResponse> RequestEarlyUnfreezeAsync(int requestId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeRequestResponse>(HttpMethod.Post,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}/early-unfreeze"), cancellationToken);

    public Task<MembershipFreezeDocumentResponse> UploadFreezeDocumentAsync(
        int requestId, Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var upload = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        upload.Add(file, "file", fileName);
        return SendAsync<MembershipFreezeDocumentResponse>(HttpMethod.Post,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}/document"), cancellationToken, upload);
    }

    public Task<bool> RemoveFreezeDocumentAsync(int requestId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}/document"), cancellationToken);

    public Task<MembershipFreezeDocumentDownload> DownloadFreezeDocumentAsync(int requestId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeDocumentDownload>(HttpMethod.Get,
            FormattableString.Invariant($"{FreezeRequestsPath}/{requestId}/document"), cancellationToken,
            readResponse: async (response, ct) => new(await response.Content.ReadAsByteArrayAsync(ct),
                (response.Content.Headers.ContentDisposition?.FileNameStar ??
                    response.Content.Headers.ContentDisposition?.FileName ?? "document").Trim('"'),
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream"));

    public Task<PagedResponse<MembershipFreezeReviewSummaryResponse>> ListFreezeReviewsAsync(
        MembershipFreezeReviewListRequest request, CancellationToken cancellationToken = default)
    {
        var path = FormattableString.Invariant($"{FreezeReviewsPath}?page={request.Page}&pageSize={request.PageSize}");
        if (!string.IsNullOrWhiteSpace(request.Type))
            path += $"&type={Uri.EscapeDataString(request.Type.Trim())}";
        return SendAsync<PagedResponse<MembershipFreezeReviewSummaryResponse>>(HttpMethod.Get, path, cancellationToken);
    }

    public Task<MembershipFreezeReviewDetailResponse> GetFreezeReviewAsync(
        int requestId, string requestType, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeReviewDetailResponse>(HttpMethod.Get,
            FormattableString.Invariant($"{FreezeReviewsPath}/{requestId}?type={Uri.EscapeDataString(requestType)}"), cancellationToken);

    public Task<MembershipFreezeReviewDetailResponse> ApproveFreezeReviewAsync(
        int requestId, string requestType, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeReviewDetailResponse>(HttpMethod.Post, FreezeReviewActionPath(requestId, requestType, "approve"), cancellationToken);

    public Task<MembershipFreezeReviewDetailResponse> RejectFreezeReviewAsync(
        int requestId, string requestType, RejectMembershipFreezeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipFreezeReviewDetailResponse>(HttpMethod.Post, FreezeReviewActionPath(requestId, requestType, "reject"),
            cancellationToken, JsonContent.Create(request));

    private static string FreezeReviewActionPath(int requestId, string requestType, string action) =>
        FormattableString.Invariant($"{FreezeReviewsPath}/{requestId}") +
        (requestType == MembershipFreezeReviewTypes.EarlyUnfreeze ? "/early-unfreeze" : "") + $"/{action}";

    public Task<IReadOnlyList<MembershipRenewalOption>> ListRenewalsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<MembershipRenewalOption>>(HttpMethod.Get, RenewalsPath, cancellationToken);

    public Task<MembershipPurchaseResponse> RenewAsync(RenewMembershipRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post, RenewalsPath, cancellationToken, JsonContent.Create(request));

    public Task<MembershipPurchaseResponse> ConfirmCashAsync(
        string orderNumber, ConfirmMembershipCashRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post,
            $"api/staff/membership-purchases/{Uri.EscapeDataString(orderNumber)}/confirm-cash", cancellationToken,
            JsonContent.Create(request));

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, CancellationToken cancellationToken, HttpContent? content = null,
        Func<HttpResponseMessage, CancellationToken, Task<T>>? readResponse = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var token = await session.GetAccessTokenAsync()
            ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return readResponse is null ? await AuthApiClient.SendAsync<T>(http, request, cancellationToken)
            : await AuthApiClient.SendAsync(http, request, readResponse, cancellationToken);
    }

    public void Dispose() => http.Dispose();
}

public sealed record MembershipFreezeDocumentDownload(byte[] Content, string FileName, string ContentType);
