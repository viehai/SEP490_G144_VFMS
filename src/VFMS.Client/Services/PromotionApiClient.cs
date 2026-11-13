using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Auth;
using VFMS.Contracts.Promotions;

namespace VFMS.Client.Services;

public sealed class PromotionApiClient(HttpClient http, AuthSession session) : IDisposable
{
    private const string BasePath = "api/manager/promotions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ResponseFields =
    [
        "id", "name", "code", "discountType", "discountValue", "minimumSpend", "maxDiscountAmount",
        "maxRedemptions", "usedCount", "startAt", "endAt", "isEnabled", "status"
    ];

    public Task<IReadOnlyList<PromotionResponse>> ListAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<PromotionResponse>>(HttpMethod.Get, BasePath, cancellationToken, list: true);

    public Task<PromotionResponse> GetAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<PromotionResponse>(HttpMethod.Get, FormattableString.Invariant($"{BasePath}/{id}"), cancellationToken);

    public Task<PromotionResponse> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PromotionResponse>(HttpMethod.Post, BasePath, cancellationToken, JsonContent.Create(request));

    public Task<PromotionResponse> UpdateAsync(int id, UpdatePromotionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PromotionResponse>(HttpMethod.Put, FormattableString.Invariant($"{BasePath}/{id}"), cancellationToken, JsonContent.Create(request));

    public Task<PromotionResponse> SetEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default) =>
        SendAsync<PromotionResponse>(HttpMethod.Patch, FormattableString.Invariant($"{BasePath}/{id}/enabled"), cancellationToken,
            JsonContent.Create(new SetPromotionEnabledRequest(isEnabled)));

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken,
        HttpContent? content = null, bool list = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var token = await session.GetAccessTokenAsync()
            ?? throw new AuthApiException("Vui lòng đăng nhập để tiếp tục. (MSG-11)", HttpStatusCode.Unauthorized);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await AuthApiClient.SendAsync(http, request, async (response, ct) =>
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = document.RootElement;
            if (list)
            {
                if (root.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Expected a promotion list.");
                foreach (var item in root.EnumerateArray())
                    ValidateResponse(item);
            }
            else
                ValidateResponse(root);

            return root.Deserialize<T>(JsonOptions) ?? throw new JsonException("Expected promotion data.");
        }, cancellationToken);
    }

    private static void ValidateResponse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || ResponseFields.Any(field => !value.TryGetProperty(field, out _)))
            throw new JsonException("The promotion response is incomplete.");
        if (value.GetProperty("name").ValueKind != JsonValueKind.String || value.GetProperty("code").ValueKind != JsonValueKind.String)
            throw new JsonException("The promotion response is incomplete.");
    }

    public void Dispose() => http.Dispose();
}
