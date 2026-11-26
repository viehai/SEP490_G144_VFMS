using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Components.Dashboards;
using VFMS.Client.Services;
using Xunit;

namespace VFMS.ClientTests;

public sealed class NotificationClientTests
{
    [Fact]
    public async Task HistoryPageChangeDisablesDropdownUntilTheRequestedPageArrives()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        await WithHistoryComponentsAsync(request =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Task.FromResult(Json(Inbox(true))); }
            if (request.RequestUri!.AbsolutePath.EndsWith("/history"))
                return request.RequestUri.Query.EndsWith("=2") ? response.Task : Task.FromResult(Json(HistoryPage(1)));
            return Task.FromResult(Json(Inbox()));
        }, async (history, dropdown, view) =>
        {
            var change = InvokeEvent(history, "LoadPage", 2);
            try
            {
                Assert.True(dropdown.Disabled);
                await InvokeEvent(dropdown, "MarkRead", 7);
                Assert.Equal(0, posts);
            }
            finally
            {
                response.TrySetResult(Json(HistoryPage(2)));
                await change;
            }
            Assert.Contains("Trang 2 riêng", WebUtility.HtmlDecode(view.ToHtmlString()));
        });
    }

    [Fact]
    public async Task DropdownMutationDisablesHistoryNavigationUntilItsReloadCompletes()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = new List<int>();
        await WithHistoryComponentsAsync(request =>
        {
            if (request.Method == HttpMethod.Post) return response.Task;
            if (request.RequestUri!.AbsolutePath.EndsWith("/history"))
            {
                var page = request.RequestUri.Query.EndsWith("=2") ? 2 : 1;
                pages.Add(page);
                return Task.FromResult(Json(HistoryPage(page)));
            }
            return Task.FromResult(Json(Inbox()));
        }, async (history, dropdown, view) =>
        {
            var save = InvokeEvent(dropdown, "MarkRead", 7);
            try
            {
                Assert.Matches("<button[^>]*disabled[^>]*>Sau</button>", WebUtility.HtmlDecode(view.ToHtmlString()));
                await InvokeEvent(history, "LoadPage", 2);
                Assert.DoesNotContain(2, pages);
            }
            finally
            {
                response.TrySetResult(Json(Inbox(true)));
                await save;
            }
            Assert.Contains("Trang 1 riêng", WebUtility.HtmlDecode(view.ToHtmlString()));
        });
    }

    [Fact]
    public async Task OlderDropdownLoadCannotRestoreUnreadStateAfterAHistoryMutationRefresh()
    {
        var stale = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inboxLoads = 0;
        await WithHistoryComponentsAsync(request =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Json(Inbox(true)));
            if (request.RequestUri!.AbsolutePath.EndsWith("/history")) return Task.FromResult(Json(HistoryPage(1)));
            inboxLoads++;
            return inboxLoads == 2 ? stale.Task : Task.FromResult(Json(Inbox(inboxLoads > 2)));
        }, async (history, dropdown, view) =>
        {
            var oldLoad = dropdown.RefreshAsync();
            try { await InvokeEvent(history, "MarkRead", 7); }
            finally
            {
                stale.TrySetResult(Json(Inbox()));
                await oldLoad;
            }
            Assert.DoesNotContain("dash-count", view.ToHtmlString());
        });
    }

    private static object HistoryPage(int page) => new
    {
        page, pageSize = 10, totalCount = 13,
        items = new[] { new { id = page, title = $"Trang {page} riêng", message = "Nội dung", sentAt = DateTime.UtcNow, read = false } }
    };

    private static Task InvokeEvent(ComponentBase component, string name, params object[] args)
    {
        var method = component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() => (Task)method.Invoke(component, args)!)), null);
    }

    [Fact]
    public async Task HistoryPageLoadsPagesFromTheServerAndShowsReadActions()
    {
        var pages = new List<int>();
        await WithHistoryAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/history"))
            {
                var page = request.RequestUri.Query.EndsWith("=2") ? 2 : 1;
                pages.Add(page);
                return Json(new { page, pageSize = 10, totalCount = 13, items = new[]
                    { new { id = page, title = $"Trang {page} riêng", message = "Nội dung", sentAt = DateTime.UtcNow, read = false } } });
            }
            return Json(Inbox());
        }, async (component, view) =>
        {
            var html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Trang 1 riêng", html);
            Assert.Contains("Đánh dấu đã đọc", html);
            Assert.Contains("Đánh dấu tất cả đã đọc", html);
            Assert.Contains("Sau", html);
            var method = component.GetType().GetMethod("LoadPage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            await ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() => (Task)method.Invoke(component, [2])!)), null);
            html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Trang 2 riêng", html);
            Assert.DoesNotContain("Trang 1 riêng", html);
            Assert.Equal(new[] { 1, 2 }, pages);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryPageShowsEmptyOrRecoverableErrorState(bool failed)
    {
        await WithHistoryAsync(request => request.RequestUri!.AbsolutePath.EndsWith("/history")
            ? failed ? new(HttpStatusCode.InternalServerError) { Content = JsonContent.Create(new { detail = "Lỗi tải thông báo" }) }
                : Json(new { page = 1, pageSize = 10, totalCount = 0, items = Array.Empty<object>() })
            : Json(Inbox()), (_, view) =>
        {
            var html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains(failed ? "Thử lại" : "Chưa có thông báo.", html);
            if (failed) Assert.Contains("role=\"alert\"", html);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DropdownOffersReadActionsAndHistoryWithoutAutoMarkingOnLoad()
    {
        var requests = new List<string>();
        await WithDropdownAsync(request =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return Json(Inbox());
        }, (_, view, _) =>
        {
            var html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Đánh dấu đã đọc", html);
            Assert.Contains("Đánh dấu tất cả đã đọc", html);
            Assert.Contains("href=\"/notifications\"", html);
            Assert.Contains("Xem tất cả", html);
            Assert.Single(requests);
            Assert.StartsWith("GET ", requests[0]);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DropdownReadActionsRefreshCountAndShowSuccessToast(bool all)
    {
        string? mutation = null;
        await WithDropdownAsync(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                mutation = request.RequestUri!.AbsolutePath;
                return Json(Inbox(read: true));
            }
            return Json(Inbox());
        }, async (component, view, toast) =>
        {
            var method = typeof(DashboardNotifications).GetMethod(all ? "MarkAllRead" : "MarkRead", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            await ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() => (Task)method.Invoke(component, all ? null : [7])!)), null);
            Assert.Equal(all ? "/api/dashboards/notifications/read-all" : "/api/dashboards/notifications/7/read", mutation);
            Assert.DoesNotContain("Chưa đọc", WebUtility.HtmlDecode(view.ToHtmlString()));
            Assert.False(Assert.Single(toast.Items).IsError);
        });
    }

    [Fact]
    public async Task DropdownReadFailureKeepsUnreadStateAndShowsErrorToast()
    {
        await WithDropdownAsync(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.InternalServerError) { Content = JsonContent.Create(new { detail = "Lỗi lưu thông báo" }) }
            : Json(Inbox()), async (component, view, toast) =>
        {
            var method = typeof(DashboardNotifications).GetMethod("MarkRead", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            await ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() => (Task)method.Invoke(component, [7])!)), null);
            Assert.Contains("Chưa đọc", WebUtility.HtmlDecode(view.ToHtmlString()));
            Assert.True(Assert.Single(toast.Items).IsError);
        });
    }

    private static object Inbox(bool read = false) => new
    {
        unread = read ? 0 : 1,
        recent = new[] { new { id = 7, title = "Thông báo riêng", message = "Nội dung", sentAt = DateTime.UtcNow, read } }
    };

    private static async Task WithDropdownAsync(Func<HttpRequestMessage, HttpResponseMessage> send,
        Func<DashboardNotifications, Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent, ToastService, Task> verify)
    {
        using var http = new HttpClient(new Handler(send)) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(new Storage(), new AuthApiClient(http));
        var activator = new Activator();
        var toast = new ToastService();
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(new VfmsApiClient(http, session)).AddSingleton(toast)
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DashboardNotifications>();
            await verify(activator.Component!, view, toast);
        });
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static async Task WithHistoryAsync(Func<HttpRequestMessage, HttpResponseMessage> send,
        Func<ComponentBase, Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent, Task> verify)
        => await WithHistoryComponentsAsync(request => Task.FromResult(send(request)), (history, _, view) => verify(history, view));

    private static async Task WithHistoryComponentsAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> send,
        Func<ComponentBase, DashboardNotifications, Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent, Task> verify)
    {
        var type = typeof(DashboardNotifications).Assembly.GetType("VFMS.Client.Pages.NotificationHistory");
        Assert.NotNull(type);
        using var http = new HttpClient(new AsyncHandler(send)) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(new Storage(), new AuthApiClient(http));
        var activator = new Activator();
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(new VfmsApiClient(http, session)).AddSingleton(new ToastService())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync(type);
            await verify(activator.History!, activator.Component!, view);
        });
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
    private sealed class Activator : IComponentActivator
    {
        public DashboardNotifications? Component { get; private set; }
        public ComponentBase? History { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)System.Activator.CreateInstance(type)!;
            if (component is DashboardNotifications dropdown) Component = dropdown;
            if (type.Name == "NotificationHistory") History = (ComponentBase)component;
            return component;
        }
    }
    private sealed class Storage : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult(default(T)!) : throw new InvalidOperationException(identifier);
    }
}
