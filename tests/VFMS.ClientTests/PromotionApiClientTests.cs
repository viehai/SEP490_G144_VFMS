using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Promotions;
using Xunit;

namespace VFMS.ClientTests;

public sealed class PromotionApiClientTests
{
    internal static readonly DateTime Start = new(2026, 11, 13, 1, 30, 0, DateTimeKind.Utc);
    internal static readonly DateTime End = Start.AddDays(10);

    [Fact]
    public async Task List_AuthenticatesAndReadsEveryConfigurationAndRuntimeField()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/manager/promotions", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Null(request.Content);
            return Task.FromResult(Json(new[] { Response() }));
        });

        var row = Assert.Single(await api.ListAsync());

        Assert.Equal(7, row.Id);
        Assert.Equal("November campaign", row.Name);
        Assert.Equal("SAVE20", row.Code);
        Assert.Equal(PromotionDiscountType.Percentage, row.DiscountType);
        Assert.Equal(20m, row.DiscountValue);
        Assert.Equal(200_000m, row.MinimumSpend);
        Assert.Equal(100_000m, row.MaxDiscountAmount);
        Assert.Equal(100, row.MaxRedemptions);
        Assert.Equal(3, row.UsedCount);
        Assert.Equal(Start, row.StartAt);
        Assert.Equal(End, row.EndAt);
        Assert.Equal(DateTimeKind.Utc, row.StartAt.Kind);
        Assert.True(row.IsEnabled);
        Assert.Equal(PromotionStatus.Active, row.Status);
    }

    [Fact]
    public async Task List_PreservesEmptyCollection()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(Array.Empty<PromotionResponse>())));
        Assert.Empty(await api.ListAsync());
    }

    [Theory]
    [InlineData(PromotionStatus.Scheduled)]
    [InlineData(PromotionStatus.Active)]
    [InlineData(PromotionStatus.Expired)]
    [InlineData(PromotionStatus.Disabled)]
    public async Task Detail_UsesDetailRouteAndPreservesEffectiveServerStatus(PromotionStatus status)
    {
        var expected = Response(status);
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/manager/promotions/7", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(expected));
        });

        Assert.Equal(expected, await api.GetAsync(7));
    }

    [Theory]
    [InlineData(PromotionDiscountType.Percentage)]
    [InlineData(PromotionDiscountType.Fixed)]
    public async Task Create_SerializesOnlyConfigurationWithStringDiscountAndUtcTimestamps(PromotionDiscountType type)
    {
        var cap = type == PromotionDiscountType.Percentage ? 100_000m : (decimal?)null;
        var discount = type == PromotionDiscountType.Percentage ? 20m : 50_000m;
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/manager/promotions", request.RequestUri!.PathAndQuery);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            AssertConfiguration(body, type, discount, cap);
            return Json(Response() with { DiscountType = type, DiscountValue = discount, MaxDiscountAmount = cap }, HttpStatusCode.Created);
        });

        var result = await api.CreateAsync(new("November campaign", "SAVE20", type, discount, 200_000m, cap, 100, Start, End, true));

        Assert.Equal(type, result.DiscountType);
        Assert.Equal(cap, result.MaxDiscountAmount);
    }

    [Fact]
    public async Task Update_SerializesEditedConfigurationAndPreservesReturnedUsage()
    {
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/manager/promotions/7", request.RequestUri!.PathAndQuery);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            AssertConfiguration(body, PromotionDiscountType.Percentage, 20m, 100_000m);
            return Json(Response() with { UsedCount = 9 });
        });

        var result = await api.UpdateAsync(7, new("November campaign", "SAVE20", PromotionDiscountType.Percentage,
            20m, 200_000m, 100_000m, 100, Start, End, true));

        Assert.Equal(9, result.UsedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetEnabled_SendsOnlyAvailabilityAndRetainsUsage(bool enabled)
    {
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/manager/promotions/7/enabled", request.RequestUri!.PathAndQuery);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            var field = Assert.Single(body.EnumerateObject());
            Assert.Equal("isEnabled", field.Name);
            Assert.Equal(enabled, field.Value.GetBoolean());
            return Json(Response() with { IsEnabled = enabled, Status = enabled ? PromotionStatus.Active : PromotionStatus.Disabled });
        });

        var result = await api.SetEnabledAsync(7, enabled);

        Assert.Equal(enabled, result.IsEnabled);
        Assert.Equal(3, result.UsedCount);
    }

    [Fact]
    public async Task Create_PreservesDuplicateCodeConflict()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new
        {
            code = "PROMOTION_CODE_EXISTS", detail = "Mã voucher đã tồn tại."
        }, HttpStatusCode.Conflict)));

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.CreateAsync(CreateRequest()));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("PROMOTION_CODE_EXISTS", error.ErrorCode);
        Assert.Equal("Mã voucher đã tồn tại.", error.Message);
    }

    [Fact]
    public async Task Update_PreservesServerFieldValidation()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new
        {
            errors = new { EndAt = new[] { "Thời gian kết thúc phải sau thời gian bắt đầu." } }
        }, HttpStatusCode.BadRequest)));

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.UpdateAsync(7,
            new("Campaign", "SAVE", PromotionDiscountType.Fixed, 50m, null, null, null, Start, Start, true)));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Thời gian kết thúc phải sau thời gian bắt đầu.", Assert.Single(error.Errors["EndAt"]));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ApiError_PreservesStatusAndErrorCode(HttpStatusCode status)
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new { code = "PROMOTION_ERROR", detail = "Request failed." }, status)));
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.GetAsync(7));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("PROMOTION_ERROR", error.ErrorCode);
    }

    [Fact]
    public async Task AnonymousSession_DoesNotSendHttpRequest()
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Unexpected request"), authenticated: false);
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.ListAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Theory]
    [InlineData("usedCount")]
    [InlineData("status")]
    [InlineData("isEnabled")]
    [InlineData("discountValue")]
    [InlineData("startAt")]
    [InlineData("maxRedemptions")]
    public async Task IncompleteResponse_IsRejectedInsteadOfInventingDefaultValues(string omitted)
    {
        var fields = JsonSerializer.SerializeToElement(Response(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .EnumerateObject().Where(field => field.Name != omitted).ToDictionary(field => field.Name, field => field.Value);
        using var api = CreateClient(_ => Task.FromResult(Json(fields)));

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.GetAsync(7));

        Assert.Contains("không hợp lệ", error.Message);
    }

    [Fact]
    public async Task List_RejectsIncompleteItem()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new[] { new { id = 7, name = "Incomplete" } })));
        await Assert.ThrowsAsync<AuthApiException>(() => api.ListAsync());
    }

    [Fact]
    public async Task NetworkFailure_UsesExistingRetryMessage()
    {
        using var api = CreateClient(_ => throw new HttpRequestException("Offline"));
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.ListAsync());
        Assert.Contains("Không kết nối", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var api = CreateClient(_ => throw new OperationCanceledException(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ListAsync(cancellation.Token));
    }

    private static void AssertConfiguration(JsonElement body, PromotionDiscountType type, decimal discount, decimal? cap)
    {
        Assert.Equal("November campaign", body.GetProperty("name").GetString());
        Assert.Equal("SAVE20", body.GetProperty("code").GetString());
        Assert.Equal(type.ToString(), body.GetProperty("discountType").GetString());
        Assert.Equal(discount, body.GetProperty("discountValue").GetDecimal());
        Assert.Equal(200_000m, body.GetProperty("minimumSpend").GetDecimal());
        Assert.Equal(cap, body.GetProperty("maxDiscountAmount").ValueKind == JsonValueKind.Null ? null : body.GetProperty("maxDiscountAmount").GetDecimal());
        Assert.Equal(100, body.GetProperty("maxRedemptions").GetInt32());
        Assert.Equal(Start, body.GetProperty("startAt").GetDateTime());
        Assert.EndsWith("Z", body.GetProperty("startAt").GetString());
        Assert.Equal(End, body.GetProperty("endAt").GetDateTime());
        Assert.True(body.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(10, body.EnumerateObject().Count());
        Assert.False(body.TryGetProperty("usedCount", out _));
        Assert.False(body.TryGetProperty("status", out _));
        Assert.False(body.TryGetProperty("actorUserId", out _));
        Assert.False(body.TryGetProperty("maxRedemptionsPerUser", out _));
    }

    internal static CreatePromotionRequest CreateRequest() =>
        new("November campaign", "SAVE20", PromotionDiscountType.Percentage, 20m, 200_000m, 100_000m, 100, Start, End, true);

    internal static PromotionResponse Response(PromotionStatus status = PromotionStatus.Active) =>
        new(7, "November campaign", "SAVE20", PromotionDiscountType.Percentage, 20m, 200_000m, 100_000m, 100, 3, Start, End, status != PromotionStatus.Disabled, status);

    internal static PromotionApiClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, bool authenticated = true)
    {
        var http = new HttpClient(new FakeHandler(handler)) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access-token", "refresh-token", "Bearer", 1800, "manager", "Manager", "manager@example.com", ["Manager"]);
        var stored = authenticated ? JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false)) : null;
        return new(http, new AuthSession(new ReadOnlyStorage(stored), new AuthApiClient(http)));
    }

    internal static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class ReadOnlyStorage(string? value) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((TValue)(object?)value!) : throw new InvalidOperationException(identifier);
    }
}
