using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Feedback;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Packages;

namespace VFMS.Client.Services;

public sealed class MembershipPlanApiClient(HttpClient http, AuthSession session) : IDisposable
{
    private const string BasePath = "api/manager/membership-plans";
    private const string PurchasesPath = "api/membership-purchases";
    private const string StaffPurchasesPath = "api/staff/membership-purchases";
    private const string RenewalsPath = "api/membership-renewals";
    private const string MembershipsPath = "api/memberships";
    private const string TransactionsPath = "api/membership-transactions";
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
        if (!string.IsNullOrWhiteSpace(request.ServiceType))
            path += $"&serviceType={Uri.EscapeDataString(request.ServiceType)}";
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

    public Task<PagedResponse<MembershipPlanCatalogItemResponse>> ListCatalogAsync(int page, CancellationToken cancellationToken = default,
        string? serviceType = null) =>
        SendAsync<PagedResponse<MembershipPlanCatalogItemResponse>>(HttpMethod.Get,
            FormattableString.Invariant($"api/membership-plans?page={page}&pageSize=9") +
            (string.IsNullOrWhiteSpace(serviceType) ? "" : $"&serviceType={Uri.EscapeDataString(serviceType)}"), cancellationToken,
            allowAnonymous: true);   // the public price list on the home page

    /// <summary>
    /// Gọi public catalog endpoint GET /api/packages?type={type}&page={page}&pageSize={pageSize}.
    /// Hỗ trợ cả người dùng nặc danh (anonymous) và người dùng đã đăng nhập.
    /// </summary>
    public async Task<PagedResponse<PackageCatalogItemResponse>> GetPackagesAsync(
        string type, int page = 1, int pageSize = 9, CancellationToken cancellationToken = default)
    {
        var path = FormattableString.Invariant($"api/packages?type={Uri.EscapeDataString(type.Trim())}&page={page}&pageSize={pageSize}");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (await session.GetAccessTokenAsync() is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await AuthApiClient.SendAsync<PagedResponse<PackageCatalogItemResponse>>(http, request, cancellationToken);
    }

    public Task<PackageDetailResponse> GetPackageDetailAsync(string type, int id, CancellationToken cancellationToken = default) =>
        SendAsync<PackageDetailResponse>(HttpMethod.Get,
            FormattableString.Invariant($"api/packages/{Uri.EscapeDataString(type.Trim())}/{id}"),
            cancellationToken, allowAnonymous: true);

    public Task<PackageReviewsResponse> GetPackageReviewsAsync(int id, int page, int? rating, CancellationToken cancellationToken = default) =>
        SendAsync<PackageReviewsResponse>(HttpMethod.Get, FormattableString.Invariant($"api/packages/{id}/reviews?page={page}") +
            (rating is int stars ? FormattableString.Invariant($"&rating={stars}") : ""), cancellationToken, allowAnonymous: true);

    public Task<MembershipPurchaseResponse> PurchaseAsync(MembershipPurchaseRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post, PurchasesPath, cancellationToken, JsonContent.Create(request), ReadPurchaseAsync);

    public Task<MembershipPurchaseResponse> GetPurchaseAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Get, $"{PurchasesPath}/{Uri.EscapeDataString(orderNumber)}", cancellationToken, readResponse: ReadPurchaseAsync);

    public Task<MembershipPurchaseResponse> ApplyMembershipVoucherAsync(string orderNumber,
        ApplyMembershipVoucherRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post,
            $"{PurchasesPath}/{Uri.EscapeDataString(orderNumber)}/voucher", cancellationToken, JsonContent.Create(request), ReadPurchaseAsync);

    public Task<MembershipPurchaseResponse> RemoveMembershipVoucherAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Delete,
            $"{PurchasesPath}/{Uri.EscapeDataString(orderNumber)}/voucher", cancellationToken, readResponse: ReadPurchaseAsync);

    public Task<MembershipPurchaseResponse> CompleteNoChargeMembershipPurchaseAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post,
            $"{PurchasesPath}/{Uri.EscapeDataString(orderNumber)}/complete-no-charge", cancellationToken, readResponse: ReadPurchaseAsync);

    public Task<IReadOnlyList<MembershipSummaryResponse>> GetMyMembershipsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<MembershipSummaryResponse>>(HttpMethod.Get, $"{MembershipsPath}/me", cancellationToken);

    public Task<MembershipDetailResponse> GetMembershipDetailAsync(int membershipId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipDetailResponse>(HttpMethod.Get,
            FormattableString.Invariant($"{MembershipsPath}/{membershipId}"), cancellationToken);

    public async Task<MembershipDetailResponse> ScheduleMembershipActivationAsync(
        int membershipId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var detail = await SendAsync<MembershipDetailResponse>(HttpMethod.Put,
            FormattableString.Invariant($"{MembershipsPath}/{membershipId}/activation"), cancellationToken,
            JsonContent.Create(new ScheduleMembershipActivationRequest(date)));
        if (detail.Membership.Status == MembershipStatuses.Active)
            await session.RefreshMemberRoleAsync(null, cancellationToken);
        return detail;
    }

    public Task<PagedResponse<MembershipTransactionSummaryResponse>> ListTransactionsAsync(
        int page, int pageSize = 10, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResponse<MembershipTransactionSummaryResponse>>(HttpMethod.Get,
            FormattableString.Invariant($"{TransactionsPath}?page={page}&pageSize={pageSize}"), cancellationToken);

    public Task<MembershipTransactionDetailResponse> GetTransactionAsync(int orderId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipTransactionDetailResponse>(HttpMethod.Get,
            FormattableString.Invariant($"{TransactionsPath}/{orderId}"), cancellationToken);

    public Task<MembershipInvoicePdfDownload> DownloadInvoicePdfAsync(int orderId, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipInvoicePdfDownload>(HttpMethod.Get,
            FormattableString.Invariant($"{TransactionsPath}/{orderId}/invoice/pdf"), cancellationToken,
            readResponse: async (response, ct) =>
            {
                if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                    throw new AuthApiException("Máy chủ chưa trả về hóa đơn PDF hợp lệ. Vui lòng thử lại sau.");
                var content = await response.Content.ReadAsByteArrayAsync(ct);
                if (content.Length < 5 || !content.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                    throw new AuthApiException("Máy chủ chưa trả về hóa đơn PDF hợp lệ. Vui lòng thử lại sau.");
                var fileName = (response.Content.Headers.ContentDisposition?.FileNameStar ??
                    response.Content.Headers.ContentDisposition?.FileName ?? "").Trim('"');
                if (fileName.Length is 0 or > 128 || !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.') ||
                    fileName.Contains("..", StringComparison.Ordinal))
                    fileName = FormattableString.Invariant($"VFMS-Invoice-{orderId}.pdf");
                return new(content, fileName, "application/pdf");
            });

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
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post, RenewalsPath, cancellationToken, JsonContent.Create(request), ReadPurchaseAsync);

    public Task<MembershipPurchaseResponse> ConfirmCashAsync(
        string orderNumber, ConfirmMembershipCashRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post,
            $"{StaffPurchasesPath}/{Uri.EscapeDataString(orderNumber)}/confirm-cash", cancellationToken,
            JsonContent.Create(request), ReadPurchaseAsync);

    public Task<PagedResponse<StaffPackageOrderResponse>> ListStaffOrdersAsync(
        StaffPackageOrderQuery query, CancellationToken cancellationToken = default)
    {
        var path = FormattableString.Invariant($"{StaffPurchasesPath}?page={query.Page}&pageSize={query.PageSize}");
        var filters = new (string Name, string? Value)[]
        {
            ("search", query.Search), ("status", query.Status), ("serviceType", query.ServiceType),
            ("method", query.Method), ("paymentStatus", query.PaymentStatus),
            ("from", query.From?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            ("to", query.To?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
        };
        foreach (var (name, value) in filters)
            if (!string.IsNullOrWhiteSpace(value))
                path += $"&{name}={Uri.EscapeDataString(value.Trim())}";

        return SendAsync<PagedResponse<StaffPackageOrderResponse>>(HttpMethod.Get, path, cancellationToken);
    }

    private static readonly JsonSerializerOptions PurchaseJsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<MembershipPurchaseResponse> ReadPurchaseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;
        // Require financial/voucher fields explicitly so an older or incomplete response cannot silently show a full-price order.
        if (root.ValueKind != JsonValueKind.Object ||
            new[] { "subtotal", "amountDue", "discountAmount", "appliedVoucher" }.Any(field => !root.TryGetProperty(field, out _)))
            throw new JsonException("The order financial summary is incomplete.");
        if (root.GetProperty("appliedVoucher") is { ValueKind: not JsonValueKind.Null } voucher &&
            (voucher.ValueKind != JsonValueKind.Object ||
             new[] { "code", "name", "discountAmount", "redemptionStatus" }.Any(field => !voucher.TryGetProperty(field, out _)) ||
             new[] { "code", "name", "redemptionStatus" }.Any(field => voucher.GetProperty(field).ValueKind != JsonValueKind.String)))
            throw new JsonException("The applied voucher snapshot is incomplete.");
        return root.Deserialize<MembershipPurchaseResponse>(PurchaseJsonOptions) ?? throw new JsonException("Expected order data.");
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, CancellationToken cancellationToken, HttpContent? content = null,
        Func<HttpResponseMessage, CancellationToken, Task<T>>? readResponse = null, bool allowAnonymous = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var token = await session.GetAccessTokenAsync();
        if (token is null && !allowAnonymous)
            throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return readResponse is null ? await AuthApiClient.SendAsync<T>(http, request, cancellationToken)
            : await AuthApiClient.SendAsync(http, request, readResponse, cancellationToken);
    }

    public void Dispose() => http.Dispose();
}

public sealed record MembershipFreezeDocumentDownload(byte[] Content, string FileName, string ContentType);

public sealed record MembershipInvoicePdfDownload(byte[] Content, string FileName, string ContentType);
