using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Users;
using Xunit;

namespace VFMS.ClientTests;

public sealed class AuthenticationTests
{
    private static LoginResponse User(string token = "access") =>
        new(token, "refresh", "Bearer", 1800, "test-user", "Test Member", "member@example.com", ["Customer"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignIn_PersistsChosenStorageModeAndPublishesIdentity(bool rememberMe)
    {
        var storage = new FakeStorage();
        LoginRequest? received = null;
        var api = Api(async request =>
        {
            Assert.Equal("/api/auth/login", request.RequestUri!.AbsolutePath);
            received = await request.Content!.ReadFromJsonAsync<LoginRequest>();
            return Json(User());
        });
        var session = new AuthSession(storage, api);
        await session.SignInAsync(new("member@example.com", "Test-Password1", rememberMe));
        var state = await session.GetAuthenticationStateAsync();

        Assert.Equal(rememberMe, received!.RememberMe);
        Assert.Equal(rememberMe, storage.RememberMe);
        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.True(state.User.IsInRole("Customer"));
        Assert.Equal("Test Member", state.User.Identity.Name);
        Assert.DoesNotContain("Test-Password1", storage.Json);
    }

    [Fact]
    public async Task ExpiredSession_RefreshesOnlyOnceAcrossConcurrentCalls()
    {
        var storage = new FakeStorage { Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow.AddMinutes(-1), true)) };
        var calls = 0;
        var api = Api(async request =>
        {
            Assert.Equal("/api/auth/refresh", request.RequestUri!.AbsolutePath);
            Assert.Equal("refresh", (await request.Content!.ReadFromJsonAsync<RefreshTokenRequest>())!.RefreshToken);
            Interlocked.Increment(ref calls);
            await Task.Yield();
            return Json(User("rotated"));
        });
        var session = new AuthSession(storage, api);
        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => session.GetAccessTokenAsync()));
        Assert.All(tokens, token => Assert.Equal("rotated", token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TwoTabsRestoreTheSameLoginAndRotateAnExpiredTokenOnce()
    {
        var storage = new FakeStorage();
        int refreshes = 0;
        using var api = Api(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/login") return Json(User());
            Assert.Equal("/api/auth/refresh", request.RequestUri.AbsolutePath);
            Assert.Equal("refresh", (await request.Content!.ReadFromJsonAsync<RefreshTokenRequest>())!.RefreshToken);
            refreshes++;
            await Task.Yield();
            return Json(User("new-access") with { RefreshToken = "new-refresh" });
        });
        using var first = new AuthSession(storage, api);
        using var second = new AuthSession(storage, api);
        await first.WatchBrowserSessionAsync();
        await first.SignInAsync(new("member@example.com", "Test-Password1", false));
        await second.WatchBrowserSessionAsync();
        Assert.True((await second.GetAuthenticationStateAsync()).User.IsInRole("Customer"));
        storage.Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow, false));

        Assert.All(await Task.WhenAll(first.GetAccessTokenAsync(), second.GetAccessTokenAsync()),
            token => Assert.Equal("new-access", token));
        Assert.Equal(1, refreshes);
    }

    [Theory]
    [InlineData("Customer", "Pending", true, false)]
    [InlineData("Customer", "Paid", false, false)]
    [InlineData("Customer", "Paid", true, true)]
    [InlineData("Member", "Paid", true, false)]
    [InlineData("Member", "Pending", true, false)]
    [InlineData("Member", "Paid", false, false)]
    public async Task PaidPersonalCheckoutNotifiesOtherTabsEvenWhenTheRoleIsUnchanged(string role, string status, bool forSelf, bool promoted)
    {
        var buyer = User() with { Roles = [role] };
        var storage = new FakeStorage { Json = JsonSerializer.Serialize(new StoredAuthSession(buyer, DateTimeOffset.UtcNow.AddMinutes(20), false)) };
        var refreshes = 0;
        using var authApi = Api(_ =>
        {
            refreshes++;
            return Task.FromResult(Json(User("member-access") with { Roles = ["Member"], RefreshToken = "member-refresh" }));
        });
        using var session = new AuthSession(storage, authApi);
        var now = DateTimeOffset.UtcNow;
        var order = new MembershipPurchaseResponse("ORD-SELF", status, status, "Cash", now, now.AddDays(1), now, 100, 100,
            [new(1, "Gym", forSelf, null, 100, true)], null, null, null, null);
        using var client = new MembershipPlanApiClient(new HttpClient(new FakeHandler(_ => Task.FromResult(Json(order))))
            { BaseAddress = new Uri("https://api.example.test/") }, session, storage);
        Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? published = null;
        session.AuthenticationStateChanged += state => published = state;
        await client.GetPurchaseAsync(order.OrderNumber);

        Assert.Equal(promoted, published is not null && (await published).User.IsInRole("Member"));
        Assert.Equal(promoted ? 1 : 0, refreshes);
        Assert.Equal(status == "Paid" && forSelf ? 1 : 0, storage.PurchaseNotifications);
        Assert.Equal(promoted ? "member-access" : "access", await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task ActivatedMembershipRefreshesTheCurrentCustomerSessionWithoutSigningInAgain()
    {
        var identity = User();
        var storage = new FakeStorage
        {
            Json = JsonSerializer.Serialize(new StoredAuthSession(identity, DateTimeOffset.UtcNow.AddMinutes(20), true))
        };
        var activated = false;
        var refreshes = 0;
        using var api = Api(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/users/me")
            {
                Assert.Equal("access", request.Headers.Authorization!.Parameter);
                return Json(new UserProfileResponse(identity.UserId, identity.FullName, identity.Email,
                    "0901234567", null, "Active", activated ? ["Member"] : ["Customer"], DateTime.UtcNow));
            }

            Assert.Equal("/api/auth/refresh", request.RequestUri.AbsolutePath);
            Assert.Equal("refresh", (await request.Content!.ReadFromJsonAsync<RefreshTokenRequest>())!.RefreshToken);
            refreshes++;
            return Json(identity with { AccessToken = "member-access", RefreshToken = "member-refresh", Roles = ["Member"] });
        });
        var session = new AuthSession(storage, api);
        Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? change = null;
        session.AuthenticationStateChanged += state => change = state;

        await session.SyncMemberRoleAsync(CancellationToken.None);
        Assert.Null(change);
        Assert.Equal(0, refreshes);

        activated = true;
        await session.SyncMemberRoleAsync(CancellationToken.None);
        await session.SyncMemberRoleAsync(CancellationToken.None);
        Assert.Equal(1, refreshes);
        Assert.True((await change!).User.IsInRole("Member"));
        Assert.Equal("member-access", await session.GetAccessTokenAsync());
        Assert.Equal("member-refresh", JsonSerializer.Deserialize<StoredAuthSession>(storage.Json!)!.User.RefreshToken);
        Assert.True(storage.RememberMe);
    }

    [Fact]
    public async Task RejectedRefresh_ClearsStoredCredentials()
    {
        var storage = new FakeStorage { Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow.AddMinutes(-1), true)) };
        var api = Api(_ => Task.FromResult(Json(new { errorCode = "AUTH_INVALID_REFRESH_TOKEN" }, HttpStatusCode.Unauthorized)));
        var state = await new AuthSession(storage, api).GetAuthenticationStateAsync();
        Assert.False(state.User.Identity!.IsAuthenticated);
        Assert.Null(storage.Json);
    }

    [Fact]
    public async Task Register_SendsContractAndReturnsSuccessfulAccount()
    {
        var api = Api(async request =>
        {
            Assert.Equal("/api/auth/register", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<RegisterRequest>();
            Assert.Equal("Test Member", body!.FullName);
            Assert.Equal(body.Password, body.ConfirmPassword);
            return Json(new RegisterResponse("test-user", body.FullName, body.Email, "Created"), HttpStatusCode.Created);
        });
        var response = await api.RegisterAsync(new("Test Member", "member@example.com", "0901234567", "Test-Password1", "Test-Password1"));
        Assert.Equal("test-user", response.UserId);
    }

    [Fact]
    public async Task ApiErrors_ReadBothContractErrorsAndValidationProblems()
    {
        var conflict = Api(_ => Task.FromResult(Json(new { errorCode = "AUTH_EMAIL_EXISTS" }, HttpStatusCode.Conflict)));
        var error = await Assert.ThrowsAsync<AuthApiException>(() => conflict.RegisterAsync(new("Test", "member@example.com", "0901234567", "Password1!", "Password1!")));
        Assert.Contains("Email này", error.Message);

        var validation = Api(_ => Task.FromResult(Json(new { errors = new { Email = new[] { "Invalid email." } } }, HttpStatusCode.BadRequest)));
        var failure = await Assert.ThrowsAsync<AuthApiException>(() => validation.LoginAsync(new("x", "y")));
        Assert.Equal("Invalid email.", Assert.Single(failure.Errors["Email"]));
    }

    [Fact]
    public async Task OfflineLogin_DoesNotPersistOrReportSuccess()
    {
        var storage = new FakeStorage();
        var api = Api(_ => throw new HttpRequestException("Offline"));
        await Assert.ThrowsAsync<AuthApiException>(() => new AuthSession(storage, api).SignInAsync(new("member@example.com", "Password1!")));
        Assert.Null(storage.Json);
    }

    [Fact]
    public async Task CorruptStoredSession_ReturnsAnonymous()
    {
        var storage = new FakeStorage { Json = "invalid-json" };
        var session = new AuthSession(storage, Api(_ => throw new InvalidOperationException("Must not request the API")));
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Null(storage.Json);
    }

    [Fact]
    public async Task MalformedSuccessfulLogin_DoesNotPersistCredentials()
    {
        var storage = new FakeStorage();
        var api = Api(_ => Task.FromResult(Json(new { message = "Not a login response" })));
        await Assert.ThrowsAsync<AuthApiException>(() => new AuthSession(storage, api).SignInAsync(new("member@example.com", "Password1!")));
        Assert.Null(storage.Json);
    }

    [Theory]
    [InlineData("https://example.com", "/")]
    [InlineData("//example.com", "/")]
    [InlineData("/\\example.com", "/")]
    [InlineData("/auth/login", "/")]
    [InlineData("/%2fexample.com", "/")]
    [InlineData("/%5cexample.com", "/")]
    [InlineData("/", "/")]
    [InlineData("/member?tab=bookings", "/member?tab=bookings")]
    public void ReturnUrl_OnlyAllowsLocalDestinations(string candidate, string expected) =>
        Assert.Equal(expected, AuthNavigation.SafeReturnUrl(candidate));

    [Fact]
    public async Task SignOut_RevokesCurrentTokenAndPublishesAnonymousState()
    {
        var storage = new FakeStorage
        {
            Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow.AddMinutes(10), true))
        };
        var calls = 0;
        var api = Api(async request =>
        {
            Assert.Equal("/api/auth/logout", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("access", request.Headers.Authorization.Parameter);
            Assert.Equal("refresh", (await request.Content!.ReadFromJsonAsync<LogoutRequest>())!.RefreshToken);
            calls++;
            return Json(new { Message = "Logged out successfully." });
        });
        var session = new AuthSession(storage, api);
        var notified = false;
        session.AuthenticationStateChanged += _ => notified = true;

        Assert.True(await session.SignOutAsync());
        Assert.Null(storage.Json);
        Assert.Null(await session.GetAccessTokenAsync());
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.True(notified);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SignOut_RefreshesExpiredAccessBeforeRevokingRotatedRefreshToken()
    {
        var storage = new FakeStorage
        {
            Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow.AddMinutes(-1), false))
        };
        var paths = new List<string>();
        var api = Api(async request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/api/auth/refresh")
                return Json(User("rotated-access") with { RefreshToken = "rotated-refresh" });
            Assert.Equal("rotated-access", request.Headers.Authorization!.Parameter);
            Assert.Equal("rotated-refresh", (await request.Content!.ReadFromJsonAsync<LogoutRequest>())!.RefreshToken);
            return Json(new { Message = "Logged out successfully." });
        });

        Assert.True(await new AuthSession(storage, api).SignOutAsync());
        Assert.Equal(new[] { "/api/auth/refresh", "/api/auth/logout" }, paths);
        Assert.Null(storage.Json);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SignOut_ApiRejectionStillEndsLocalSession(HttpStatusCode status)
    {
        var storage = new FakeStorage
        {
            Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow.AddMinutes(10), true))
        };
        var api = Api(_ => Task.FromResult(Json(new { Message = "Unavailable" }, status)));
        var session = new AuthSession(storage, api);
        Assert.False(await session.SignOutAsync());
        Assert.Null(storage.Json);
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task SignOut_OfflineStillClearsRememberedSession()
    {
        var storage = new FakeStorage
        {
            Json = JsonSerializer.Serialize(new StoredAuthSession(User(), DateTimeOffset.UtcNow.AddMinutes(-1), true))
        };
        var api = Api(_ => throw new HttpRequestException("Offline"));
        var session = new AuthSession(storage, api);
        Assert.False(await session.SignOutAsync());
        Assert.Null(storage.Json);
        Assert.Null(await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task SignOut_AnonymousDoesNotRequestApi()
    {
        var storage = new FakeStorage();
        var session = new AuthSession(storage, Api(_ => throw new InvalidOperationException("Must not call the API")));
        Assert.True(await session.SignOutAsync());
        Assert.Null(storage.Json);
    }

    private static AuthApiClient Api(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) =>
        new(new HttpClient(new FakeHandler(handler)) { BaseAddress = new Uri("https://api.example.test/") });
    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class FakeStorage : IJSRuntime
    {
        private readonly SemaphoreSlim _browserLock = new(1, 1);
        public string? Json { get; set; }
        public bool RememberMe { get; private set; }
        public int PurchaseNotifications { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "vfmsAuthStorage.lock") await _browserLock.WaitAsync(cancellationToken);
            if (identifier == "vfmsAuthStorage.unlock") _browserLock.Release();
            if (identifier == "vfmsAuthStorage.read") return (TValue)(object?)Json!;
            if (identifier == "vfmsAuthStorage.write") { Json = (string)args![0]!; RememberMe = (bool)args[1]!; }
            if (identifier == "vfmsAuthStorage.clear") Json = null;
            if (identifier == "vfmsMembershipUpdates.publish") PurchaseNotifications++;
            return default!;
        }
    }
}
