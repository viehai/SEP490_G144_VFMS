using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client;
using VFMS.Client.Auth;
using VFMS.Client.Components.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed class RouterAuthenticationTests
{
    [Theory]
    [InlineData("Admin", "/admin/dashboard", "admin")]
    [InlineData("Manager", "/manager/dashboard", "manager")]
    [InlineData("Staff", "/staff/dashboard", "staff")]
    [InlineData("PT", "/pt/dashboard", "pt")]
    public async Task RenderedWorkspaceLogout_SafelyDisposesNestedAuthorizationViewsAndGuardsTheRoute(
        string role, string dashboard, string variant)
    {
        var identity = new LoginResponse("synthetic-access", "synthetic-refresh", "Bearer", 1800,
            "synthetic-user", "Synthetic User", "synthetic@example.test", [role]);
        var storage = new Storage(JsonSerializer.Serialize(
            new StoredAuthSession(identity, DateTimeOffset.UtcNow.AddMinutes(10), true)));
        var logoutCalls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/membership-plans")   // the home page's public price list
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PagedResponse<MembershipPlanCatalogItemResponse>(1, 9, 0, []))
                };
            if (request.RequestUri!.AbsolutePath == "/api/yoga/schedule")   // the home page's yoga classes
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) };
            Assert.Equal("/api/auth/logout", request.RequestUri!.AbsolutePath);
            logoutCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { Message = "Logged out." }) };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(storage, new AuthApiClient(http));
        var navigation = new Navigation(dashboard);
        var components = new CapturingActivator();
        var registrations = new ServiceCollection().AddLogging().AddAuthorizationCore()
            .AddSingleton(session).AddSingleton<AuthenticationStateProvider>(session)
            .AddSingleton(new MembershipPlanApiClient(http, session))
            .AddSingleton(new VfmsApiClient(http, session))
            .AddSingleton<IJSRuntime>(storage).AddSingleton<NavigationManager>(navigation)
            .AddSingleton<IComponentActivator>(components)
            .AddSingleton<INavigationInterception>(new NoNavigationInterception())
            .AddSingleton<IScrollToLocationHash>(new NoScrollToLocationHash());

        // Preserve the old host registration as a regression trigger. App's explicit component
        // cascade must safely own descendant subscriptions even when this service is present.
        registrations.AddCascadingAuthenticationState();
        using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var rendered = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<App>());
        var initial = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains($"workspace-shell--{variant}", initial);
        var logout = Assert.Single(components.Instances.OfType<LogoutButton>());

        // Invoke the actual rendered button's handler on its renderer, so the real Router,
        // AuthorizeRouteView, WorkspaceShell, and nested AuthorizeViews participate in disposal.
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var handler = typeof(LogoutButton).GetMethod("SignOutAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)handler.Invoke(logout, null)!;
        });

        Assert.Equal(1, logoutCalls);
        Assert.Null(storage.Json);
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal("/", new Uri(navigation.Uri).AbsolutePath);
        var home = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.DoesNotContain("workspace-shell--", home);
        Assert.Contains("ĐĂNG NHẬP", home);

        var denied = await renderer.Dispatcher.InvokeAsync(() =>
        {
            navigation.NavigateTo(dashboard);
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
        // Signed out, a protected route goes straight to the login page and comes back after sign-in.
        var login = new Uri(navigation.Uri);
        Assert.Equal("/auth/login", login.AbsolutePath);
        Assert.Equal($"?returnUrl={Uri.EscapeDataString(dashboard)}", login.Query);
        Assert.Contains("auth-shell", denied);
        Assert.DoesNotContain("workspace-shell--", denied);
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Instances { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Instances.Add(component);
            return component;
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string path) => Initialize("https://client.example.test/", "https://client.example.test" + path);
        protected override void NavigateToCore(string uri, bool forceLoad) => ChangeLocation(uri);
        protected override void NavigateToCore(string uri, NavigationOptions options) => ChangeLocation(uri);
        private void ChangeLocation(string uri)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }

    private sealed class NoNavigationInterception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
    }

    private sealed class NoScrollToLocationHash : IScrollToLocationHash
    {
        public Task RefreshScrollPositionForHash(string locationAbsolute) => Task.CompletedTask;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class Storage(string json) : IJSRuntime
    {
        public string? Json { get; private set; } = json;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "vfmsAuthStorage.read") return ValueTask.FromResult((TValue)(object?)Json!);
            if (identifier == "vfmsAuthStorage.clear") Json = null;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
