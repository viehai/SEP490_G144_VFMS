using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Components.Auth;
using VFMS.Client.Pages.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using Xunit;

namespace VFMS.ClientTests;

public sealed class LoginNavigationTests
{
    [Theory]
    [InlineData("Admin", "/admin/dashboard")]
    [InlineData("Manager", "/manager/dashboard")]
    [InlineData("Staff", "/staff/dashboard")]
    [InlineData("PT", "/pt/dashboard")]
    [InlineData("Customer", "/")]
    [InlineData("Member", "/")]
    public async Task SuccessfulLogin_WaitsForStorageAndPublishedIdentityBeforeNavigating(string role, string destination)
    {
        var events = new List<string>();
        var persist = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new Storage(events) { WriteGate = persist.Task };
        var session = Session(storage, _ => Task.FromResult(Json(User(role))));
        Task<AuthenticationState>? published = null;
        session.AuthenticationStateChanged += state => { published = state; events.Add("notified"); };
        var navigation = new Navigation();
        navigation.BeforeNavigate = _ =>
        {
            Assert.NotNull(storage.Json);
            Assert.NotNull(published);
            Assert.True(published!.IsCompletedSuccessfully);
            events.Add("navigated");
        };
        using var services = Services(session, navigation);
        var page = LoginPage(services, session, navigation);

        var submit = InvokeAsync(page, "SubmitAsync");
        await storage.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(navigation.Visits);
        Assert.False(submit.IsCompleted);
        persist.SetResult(true);
        await submit;
        Assert.True((await published!).User.IsInRole(role));

        Assert.Equal(new[] { "stored", "notified", "navigated" }, events);
        var visit = Assert.Single(navigation.Visits);
        Assert.Equal(destination, visit.Destination);
        Assert.True(visit.Replace);
        Assert.True((await session.GetAuthenticationStateAsync()).User.IsInRole(role));
        Assert.DoesNotContain("Synthetic-Password1!", storage.Json);
    }

    [Fact]
    public async Task RejectedLogin_KeepsLoginPageAndDoesNotPublishSuccessOrStoreCredentials()
    {
        var storage = new Storage([]);
        var session = Session(storage, _ => Task.FromResult(Json(new { errorCode = "AUTH_INVALID_CREDENTIALS" }, HttpStatusCode.Unauthorized)));
        var notifications = 0;
        session.AuthenticationStateChanged += _ => notifications++;
        var navigation = new Navigation();
        using var services = Services(session, navigation);
        var page = LoginPage(services, session, navigation);

        await InvokeAsync(page, "SubmitAsync");

        Assert.Empty(navigation.Visits);
        Assert.Null(storage.Json);
        Assert.Equal(0, notifications);
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.False(string.IsNullOrWhiteSpace((string?)Field(page, "_error")));
    }

    [Theory]
    [InlineData("Admin", "/admin/dashboard")]
    [InlineData("Manager", "/manager/dashboard")]
    [InlineData("Staff", "/staff/dashboard")]
    [InlineData("PT", "/pt/dashboard")]
    public async Task RestoredWorkspaceSession_RedirectsWithoutAnotherLoginOrRenderingLoginForm(string role, string destination)
    {
        var storage = new Storage([]) { Json = Stored(role) };
        var session = Session(storage, _ => throw new InvalidOperationException("An unexpired restored session must not call the API."));
        var navigation = new Navigation();
        using var services = Services(session, navigation);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Login>()).ToHtmlString());

        Assert.Equal(destination, Assert.Single(navigation.Visits).Destination);
        Assert.DoesNotContain("login-email", html);
        Assert.DoesNotContain("type=\"submit\"", html);
    }

    [Fact]
    public async Task RestoringSession_ShowsProgressUntilStoredIdentityIsKnown()
    {
        var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new Storage([]) { Json = Stored("Admin"), ReadGate = read.Task };
        var session = Session(storage, _ => throw new InvalidOperationException("Must not call the API."));
        var navigation = new Navigation();
        using var services = Services(session, navigation);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var pending = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<Login>());
        await storage.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = await renderer.Dispatcher.InvokeAsync(pending.ToHtmlString);
        Assert.Contains("role=\"status\"", before);
        Assert.DoesNotContain("login-email", before);
        Assert.Empty(navigation.Visits);

        read.SetResult(true);
        await pending.QuiescenceTask;
        Assert.Equal("/admin/dashboard", Assert.Single(navigation.Visits).Destination);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task RestoredCustomerOrMemberSession_DoesNotForceWorkspaceNavigation(string role)
    {
        var storage = new Storage([]) { Json = Stored(role) };
        var session = Session(storage, _ => throw new InvalidOperationException("Must not call the API."));
        var navigation = new Navigation();
        using var services = Services(session, navigation);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Login>()).ToHtmlString());

        Assert.Empty(navigation.Visits);
        Assert.Contains("href=\"/\"", html);
        Assert.DoesNotContain("login-email", html);
    }

    [Fact]
    public async Task LogoutThenDifferentAccount_UsesNewRoleAndRejectsPreviousWorkspaceReturnUrl()
    {
        var storage = new Storage([]) { Json = Stored("Admin") };
        var paths = new List<string>();
        var session = Session(storage, request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath == "/api/auth/logout"
                ? Json(new { Message = "Logged out successfully." }) : Json(User("Staff")));
        });
        var navigation = new Navigation();
        using var services = Services(session, navigation);
        Assert.True((await session.GetAuthenticationStateAsync()).User.IsInRole("Admin"));
        var logout = new LogoutButton();
        Inject(logout, "Session", session);
        Inject(logout, "Navigation", navigation);

        await InvokeAsync(logout, "SignOutAsync");
        Assert.Null(storage.Json);
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal("/", Assert.Single(navigation.Visits).Destination);

        var page = LoginPage(services, session, navigation);
        Inject(page, nameof(Login.ReturnUrl), "/admin/dashboard");
        await InvokeAsync(page, "SubmitAsync");

        Assert.Equal(new[] { "/api/auth/logout", "/api/auth/login" }, paths);
        Assert.Equal("/staff/dashboard", navigation.Visits.Last().Destination);
        var state = await session.GetAuthenticationStateAsync();
        Assert.True(state.User.IsInRole("Staff"));
        Assert.False(state.User.IsInRole("Admin"));
    }

    private static Login LoginPage(IServiceProvider services, AuthSession session, Navigation navigation)
    {
        var page = new Login();
        Inject(page, "Session", session);
        Inject(page, "Navigation", navigation);
        Inject(page, "AuthorizationPolicies", services.GetRequiredService<IAuthorizationPolicyProvider>());
        Inject(page, "Authorization", services.GetRequiredService<IAuthorizationService>());
        var model = Field(page, "_model")!;
        model.GetType().GetProperty("Email")!.SetValue(model, "synthetic@example.test");
        model.GetType().GetProperty("Password")!.SetValue(model, "Synthetic-Password1!");
        return page;
    }

    private static ServiceProvider Services(AuthSession session, Navigation navigation)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorizationCore()
            .AddSingleton(session).AddSingleton<AuthenticationStateProvider>(session)
            .AddSingleton<NavigationManager>(navigation);
        services.AddCascadingAuthenticationState();
        return services.BuildServiceProvider();
    }

    // Invoke the actual component event handler without adding a component-testing package.
    private static Task InvokeAsync(object component, string method) =>
        (Task)component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;

    private static object? Field(object component, string name) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component);

    private static void Inject(object component, string name, object value) =>
        component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);

    private static LoginResponse User(string role) =>
        new("synthetic-access", "synthetic-refresh", "Bearer", 1800, "synthetic-user", "Synthetic User", "synthetic@example.test", [role]);

    private static string Stored(string role) =>
        JsonSerializer.Serialize(new StoredAuthSession(User(role), DateTimeOffset.UtcNow.AddMinutes(10), true));

    private static AuthSession Session(IJSRuntime storage, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) =>
        new(storage, new AuthApiClient(new HttpClient(new Handler(handler)) { BaseAddress = new Uri("https://api.example.test/") }));

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://client.example.test/", "https://client.example.test/auth/login");
        public List<(string Destination, bool Replace)> Visits { get; } = [];
        public Action<string>? BeforeNavigate { get; set; }
        protected override void NavigateToCore(string uri, bool forceLoad) => Record(uri, replace: false);
        protected override void NavigateToCore(string uri, NavigationOptions options) => Record(uri, options.ReplaceHistoryEntry);
        private void Record(string destination, bool replace)
        {
            BeforeNavigate?.Invoke(destination);
            Visits.Add((destination, replace));
            Uri = ToAbsoluteUri(destination).ToString();
        }
    }

    private sealed class Storage(List<string> events) : IJSRuntime
    {
        public string? Json { get; set; }
        public Task? ReadGate { get; init; }
        public Task? WriteGate { get; init; }
        public TaskCompletionSource<bool> ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "vfmsAuthStorage.read")
            {
                ReadStarted.TrySetResult(true);
                if (ReadGate is not null) await ReadGate.WaitAsync(cancellationToken);
                return (TValue)(object?)Json!;
            }
            if (identifier == "vfmsAuthStorage.write")
            {
                WriteStarted.TrySetResult(true);
                if (WriteGate is not null) await WriteGate.WaitAsync(cancellationToken);
                Json = (string)args![0]!;
                events.Add("stored");
            }
            if (identifier == "vfmsAuthStorage.clear") Json = null;
            return default!;
        }
    }
}
