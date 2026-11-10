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

    public Task<IReadOnlyList<MembershipRenewalOption>> ListRenewalsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<MembershipRenewalOption>>(HttpMethod.Get, RenewalsPath, cancellationToken);

    public Task<MembershipPurchaseResponse> RenewAsync(RenewMembershipRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post, RenewalsPath, cancellationToken, JsonContent.Create(request));

    public Task<MembershipPurchaseResponse> ConfirmCashAsync(
        string orderNumber, ConfirmMembershipCashRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MembershipPurchaseResponse>(HttpMethod.Post,
            $"api/staff/membership-purchases/{Uri.EscapeDataString(orderNumber)}/confirm-cash", cancellationToken,
            JsonContent.Create(request));

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var token = await session.GetAccessTokenAsync()
            ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục.", HttpStatusCode.Unauthorized);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await AuthApiClient.SendAsync<T>(http, request, cancellationToken);
    }

    public void Dispose() => http.Dispose();
}
