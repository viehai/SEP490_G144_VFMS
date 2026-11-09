using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed class MembershipPlanApiClientTests
{
    [Fact]
    public async Task List_SendsBearerTokenAndEncodedFilters()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/manager/membership-plans", request.RequestUri!.AbsolutePath);
            Assert.Equal("?page=2&pageSize=20&search=Gym%20%26%20Yoga&status=ACTIVE", request.RequestUri.Query);
            return Task.FromResult(Json(new PagedResponse<MembershipPlanResponse>(2, 20, 21, [Plan(MembershipPlanStatuses.Active)])));
        });

        var page = await api.ListAsync(new() { Page = 2, Search = " Gym & Yoga ", Status = MembershipPlanStatuses.Active });

        Assert.Equal(3, Assert.Single(page.Items).SubscriberCount);
    }

    [Fact]
    public async Task Status_SendsTargetStatusUsingPatch()
    {
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/manager/membership-plans/7/status", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<MembershipPlanStatusRequest>();
            Assert.Equal(MembershipPlanStatuses.Inactive, body!.Status);
            return Json(Plan(MembershipPlanStatuses.Inactive));
        });

        var result = await api.ChangeStatusAsync(7, MembershipPlanStatuses.Inactive);

        Assert.Equal(MembershipPlanStatuses.Inactive, result.Status);
    }

    [Theory]
    [InlineData(true, "The plan has historical usage and is inactive.")]
    [InlineData(false, "The plan was permanently deleted.")]
    public async Task Delete_PreservesServerOutcome(bool deactivated, string message)
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/manager/membership-plans/7", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new MembershipPlanDeleteResponse(deactivated, message)));
        });

        var result = await api.DeleteAsync(7);

        Assert.Equal(deactivated, result.Deactivated);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task Create_ExposesServerFieldErrors()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(Json(new { errors = new { Name = new[] { "The plan name is required." } } }, HttpStatusCode.BadRequest));
        });

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.CreateAsync(new()));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("The plan name is required.", Assert.Single(error.Errors["Name"]));
    }

    [Fact]
    public async Task AnonymousSession_DoesNotSendRequest()
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Unexpected API request."), authenticated: false);

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.ListAsync(new()));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Purchase_SendsOptionsWithoutPriceAndReadsAuthoritativePendingOrder()
    {
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/membership-purchases", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VietQR", body.GetProperty("paymentMethod").GetString());
            Assert.False(body.TryGetProperty("purchaserUserId", out _));
            Assert.False(body.TryGetProperty("amountDue", out _));
            var item = body.GetProperty("items")[0];
            Assert.Equal(7, item.GetProperty("planId").GetInt32());
            Assert.Equal("gift@example.com", item.GetProperty("beneficiaryEmail").GetString());
            Assert.False(item.TryGetProperty("unitPrice", out _));
            var now = DateTimeOffset.UtcNow;
            return Json(new MembershipPurchaseResponse("ORD-TEST", "Pending", "Pending", "VietQR", now,
                now.AddMinutes(15), now, 500_000m, 500_000m, [], null, null, null, null), HttpStatusCode.Created);
        });

        var purchase = await api.PurchaseAsync(new([new(7, "gift@example.com")], MembershipPaymentMethods.VietQr));
        Assert.Equal(MembershipPurchaseStatuses.Pending, purchase.OrderStatus);
        Assert.Equal(500_000m, purchase.AmountDue);
    }

    [Fact]
    public async Task ListRenewals_UsesMemberSessionAndReadsServerEligibility()
    {
        var end = new DateOnly(2026, 12, 31);
        MembershipRenewalOption[] options =
        [
            new(7, "Gym", "Active", end, end.AddDays(1), 500_000m, true, null, null),
            new(8, "Gym Plus", "Frozen", end, null, null, false,
                MembershipPurchaseMessages.RenewalBlockedCode, MembershipPurchaseMessages.RenewalBlocked)
        ];
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/membership-renewals", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            return Task.FromResult(Json(options));
        }, role: "Member");

        Assert.Collection(await api.ListRenewalsAsync(),
            active =>
            {
                Assert.True(active.Eligible);
                Assert.Equal(end.AddDays(1), active.RenewalStartDate);
                Assert.Equal(500_000m, active.Price);
            },
            frozen =>
            {
                Assert.False(frozen.Eligible);
                Assert.Equal(MembershipPurchaseMessages.RenewalBlockedCode, frozen.MessageCode);
            });
    }

    [Fact]
    public async Task Renew_SendsOnlyMembershipIdAndReadsSharedCheckoutResponse()
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(2027, 1, 1);
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/membership-renewals", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            var membershipId = Assert.Single(body.EnumerateObject());
            Assert.Equal("membershipId", membershipId.Name);
            Assert.Equal(7, membershipId.Value.GetInt32());
            return Json(new MembershipPurchaseResponse("ORD-RENEWAL", "Pending", "Pending", "VietQR", now,
                now.AddMinutes(15), now, 500_000m, 500_000m, [new(12, "Gym", true, start, 500_000m)],
                new("https://qr.example.test/", "BANK", "123", "VFMS", "ORDRENEWAL"), null, null, null),
                HttpStatusCode.Created);
        }, role: "Member");

        var purchase = await api.RenewAsync(new(7));

        Assert.Equal(MembershipPurchaseStatuses.Pending, purchase.OrderStatus);
        Assert.Equal(500_000m, purchase.AmountDue);
        Assert.Equal(start, Assert.Single(purchase.Items).ActivationDate);
        Assert.Equal("ORDRENEWAL", purchase.VietQr?.TransferContent);
    }

    [Fact]
    public async Task Renew_PreservesPaymentErrorCodeForOnlineOnlyCheckout()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(
            new { errorCode = "PAYMENT_UNAVAILABLE" }, HttpStatusCode.ServiceUnavailable)), role: "Member");

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.RenewAsync(new(7)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal("PAYMENT_UNAVAILABLE", error.ErrorCode);
    }

    private static MembershipPlanResponse Plan(string status) => new(7, "Gym", null, 30, 500_000m, status, 3, null);

    private static MembershipPlanApiClient CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, bool authenticated = true, string role = "Manager")
    {
        var http = new HttpClient(new FakeHandler(handler)) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access-token", "refresh-token", "Bearer", 1800, "user", role, "user@example.com", [role]);
        var storedSession = authenticated ? JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false)) : null;
        return new MembershipPlanApiClient(http, new AuthSession(new ReadOnlyStorage(storedSession), new AuthApiClient(http)));
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

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
