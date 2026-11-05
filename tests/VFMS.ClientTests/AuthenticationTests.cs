using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
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
        public string? Json { get; set; }
        public bool RememberMe { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "vfmsAuthStorage.read") return ValueTask.FromResult((TValue)(object?)Json!);
            if (identifier == "vfmsAuthStorage.write") { Json = (string)args![0]!; RememberMe = (bool)args[1]!; }
            if (identifier == "vfmsAuthStorage.clear") Json = null;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
