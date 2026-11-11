using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Procurement;
using VFMS.Contracts.Supplements;

namespace VFMS.Client.Services;

/// <summary>
/// Equipment (UC-71..76), purchase proposals (UC-74/75), supplement products (UC-77/83) and orders (UC-78..82).
/// </summary>
public sealed class OperationsApiClient(HttpClient http, AuthSession session) : IDisposable
{
    private const string EquipmentPath = "api/equipment";
    private const string CategoriesPath = "api/equipment-categories";
    private const string IssuesPath = "api/equipment-issues";
    private const string ProposalsPath = "api/purchase-proposals";
    private const string ProductsPath = "api/supplement-products";
    private const string OrdersPath = "api/supplement-orders";

    // ── Equipment ────────────────────────────────────────────────
    public Task<PagedResponse<EquipmentResponse>> GetEquipmentAsync(EquipmentListRequest request, CancellationToken ct = default) =>
        SendAsync<PagedResponse<EquipmentResponse>>(HttpMethod.Get, WithQuery(EquipmentPath,
            ("search", request.Search), ("categoryId", Number(request.CategoryId)), ("status", request.Status),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), ct);

    public Task<EquipmentResponse> CreateEquipmentAsync(EquipmentRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentResponse>(HttpMethod.Post, EquipmentPath, ct, JsonContent.Create(request));

    public Task<EquipmentResponse> UpdateEquipmentAsync(int id, EquipmentRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentResponse>(HttpMethod.Put, $"{EquipmentPath}/{id}", ct, JsonContent.Create(request));

    public Task<EquipmentResponse> ChangeEquipmentStatusAsync(int id, EquipmentStatusRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentResponse>(HttpMethod.Patch, $"{EquipmentPath}/{id}/status", ct, JsonContent.Create(request));

    public Task<List<EquipmentCategoryResponse>> GetCategoriesAsync(CancellationToken ct = default) =>
        SendAsync<List<EquipmentCategoryResponse>>(HttpMethod.Get, CategoriesPath, ct);

    public Task<EquipmentCategoryResponse> CreateCategoryAsync(EquipmentCategoryRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentCategoryResponse>(HttpMethod.Post, CategoriesPath, ct, JsonContent.Create(request));

    public Task<EquipmentCategoryResponse> UpdateCategoryAsync(int id, EquipmentCategoryRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentCategoryResponse>(HttpMethod.Put, $"{CategoriesPath}/{id}", ct, JsonContent.Create(request));

    /// <summary>The API answers 204 with no body, which <see cref="AuthApiClient"/> cannot read, so this one is sent directly.</summary>
    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{CategoriesPath}/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await session.GetAccessTokenAsync()
            ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized));
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException) { throw new AuthApiException("Không thể kết nối máy chủ. Vui lòng thử lại."); }
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            string? code = null;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                code = document.RootElement.TryGetProperty("errorCode", out var value) ? value.GetString()
                    : document.RootElement.TryGetProperty("code", out var problem) ? problem.GetString() : null;
            }
            catch (System.Text.Json.JsonException) { }
            throw new AuthApiException(OperationsText.ErrorMessage(code) ?? "Không thể xóa danh mục. Vui lòng thử lại.", response.StatusCode, null, code);
        }
    }

    // ── Equipment issues ─────────────────────────────────────────
    public Task<EquipmentIssueResponse> ReportIssueAsync(ReportEquipmentIssueRequest request, CancellationToken ct = default) =>
        SendAsync<EquipmentIssueResponse>(HttpMethod.Post, IssuesPath, ct, JsonContent.Create(request));

    public Task<PagedResponse<EquipmentIssueResponse>> GetIssuesAsync(EquipmentIssueListRequest request, bool mine = false, CancellationToken ct = default) =>
        SendAsync<PagedResponse<EquipmentIssueResponse>>(HttpMethod.Get, WithQuery(mine ? $"{IssuesPath}/mine" : IssuesPath,
            ("status", request.Status), ("equipmentId", Number(request.EquipmentId)),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), ct);

    /// <summary>
    /// Runs an issue action. Staff: start, resolve, escalate, mark-safe.
    /// Manager: repair/start, repair/confirm-safe, repair/not-safe, repair/dispose.
    /// </summary>
    public Task<EquipmentIssueResponse> IssueActionAsync(int id, string action, string? note, CancellationToken ct = default) =>
        SendAsync<EquipmentIssueResponse>(HttpMethod.Post, $"{IssuesPath}/{id}/{action}", ct,
            note is null ? null : JsonContent.Create(new EquipmentIssueActionRequest(note)));

    // ── Purchase proposals ───────────────────────────────────────
    public Task<PagedResponse<PurchaseProposalResponse>> GetProposalsAsync(PurchaseProposalListRequest request, bool mine, CancellationToken ct = default) =>
        SendAsync<PagedResponse<PurchaseProposalResponse>>(HttpMethod.Get, WithQuery(mine ? $"{ProposalsPath}/mine" : ProposalsPath,
            ("status", request.Status), ("search", request.Search),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), ct);

    public Task<PurchaseProposalResponse> CreateProposalAsync(PurchaseProposalRequest request, CancellationToken ct = default) =>
        SendAsync<PurchaseProposalResponse>(HttpMethod.Post, ProposalsPath, ct, JsonContent.Create(request));

    public Task<PurchaseProposalResponse> UpdateProposalAsync(int id, PurchaseProposalRequest request, CancellationToken ct = default) =>
        SendAsync<PurchaseProposalResponse>(HttpMethod.Put, $"{ProposalsPath}/{id}", ct, JsonContent.Create(request));

    /// <summary>Staff: submit. Manager: approve, reject, request-revision (with a note), complete (no body).</summary>
    public Task<PurchaseProposalResponse> ProposalActionAsync(int id, string action, string? note = null, CancellationToken ct = default) =>
        SendAsync<PurchaseProposalResponse>(HttpMethod.Post, $"{ProposalsPath}/{id}/{action}", ct,
            action is "approve" or "reject" or "request-revision" ? JsonContent.Create(new PurchaseProposalReviewRequest(note)) : null);

    // ── Supplement products ──────────────────────────────────────
    public Task<PagedResponse<SupplementProductResponse>> GetProductsAsync(SupplementProductListRequest request, bool manage, CancellationToken ct = default) =>
        SendAsync<PagedResponse<SupplementProductResponse>>(HttpMethod.Get, WithQuery(manage ? $"{ProductsPath}/manage" : ProductsPath,
            ("search", request.Search), ("category", request.Category), ("status", request.Status),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), ct);

    public Task<List<string>> GetProductCategoriesAsync(CancellationToken ct = default) =>
        SendAsync<List<string>>(HttpMethod.Get, $"{ProductsPath}/categories", ct);

    public Task<SupplementProductResponse> CreateProductAsync(SupplementProductRequest request, CancellationToken ct = default) =>
        SendAsync<SupplementProductResponse>(HttpMethod.Post, ProductsPath, ct, JsonContent.Create(request));

    public Task<SupplementProductResponse> UpdateProductAsync(int id, SupplementProductRequest request, CancellationToken ct = default) =>
        SendAsync<SupplementProductResponse>(HttpMethod.Put, $"{ProductsPath}/{id}", ct, JsonContent.Create(request));

    public Task<SupplementProductResponse> ChangeProductStatusAsync(int id, string status, CancellationToken ct = default) =>
        SendAsync<SupplementProductResponse>(HttpMethod.Patch, $"{ProductsPath}/{id}/status", ct,
            JsonContent.Create(new SupplementProductStatusRequest(status)));

    // ── Supplement orders ────────────────────────────────────────
    public Task<SupplementOrderResponse> PlaceOrderAsync(PlaceSupplementOrderRequest request, CancellationToken ct = default) =>
        SendAsync<SupplementOrderResponse>(HttpMethod.Post, OrdersPath, ct, JsonContent.Create(request));

    public Task<PagedResponse<SupplementOrderResponse>> GetOrdersAsync(SupplementOrderListRequest request, bool mine, CancellationToken ct = default) =>
        SendAsync<PagedResponse<SupplementOrderResponse>>(HttpMethod.Get, WithQuery(mine ? $"{OrdersPath}/mine" : OrdersPath,
            ("status", request.Status), ("from", Date(request.From)), ("to", Date(request.To)),
            ("fulfillmentType", request.FulfillmentType), ("search", request.Search),
            ("page", Number(request.Page)), ("pageSize", Number(request.PageSize))), ct);

    public Task<PagedResponse<SupplementOrderResponse>> GetOngoingOrdersAsync(int page, int pageSize, CancellationToken ct = default) =>
        SendAsync<PagedResponse<SupplementOrderResponse>>(HttpMethod.Get, WithQuery($"{OrdersPath}/mine/ongoing",
            ("page", Number(page)), ("pageSize", Number(pageSize))), ct);

    /// <summary>Customer: cancel. Staff/Manager: process, ready-for-pickup, out-for-delivery (with courier), complete.</summary>
    public Task<SupplementOrderResponse> OrderActionAsync(int id, string action, SupplementOutForDeliveryRequest? delivery = null, CancellationToken ct = default) =>
        SendAsync<SupplementOrderResponse>(HttpMethod.Post, $"{OrdersPath}/{id}/{action}", ct,
            delivery is null ? null : JsonContent.Create(delivery));

    // ── Plumbing ─────────────────────────────────────────────────
    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string WithQuery(string path, params (string Name, string? Value)[] parameters)
    {
        var query = parameters.Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!.Trim())}");
        return $"{path}?{string.Join('&', query)}";
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var token = await session.GetAccessTokenAsync()
            ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            return await AuthApiClient.SendAsync<T>(http, request, cancellationToken);
        }
        catch (AuthApiException error) when (error.ErrorCode is { } code && OperationsText.ErrorMessage(code) is { } message)
        {
            throw new AuthApiException(message, error.StatusCode, error.Errors, code);
        }
    }

    public void Dispose() => http.Dispose();
}
