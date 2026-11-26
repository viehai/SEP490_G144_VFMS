using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Pages.Manager.Recruitment;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using Xunit;

namespace VFMS.ClientTests;

public sealed class TrainerSetupEligibilityPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagerResendButtonUsesServerCapability(bool eligible)
    {
        var application = new TrainerApplicationResponse(7, "TR-7", "Hired", "Coach", "coach@example.test", "0900000001",
            new(1990, 1, 1), "Other", "Strength", 3, "Coach biography", DateTime.UtcNow, 1, null, "coach", [], TrainerRole: "PT");
        var payload = JsonSerializer.SerializeToNode(application, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        payload["canResendSetup"] = eligible;
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/trainers/applications/7" => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) },
            "/api/trainers/applications" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new PagedResponse<TrainerApplicationResponse>(1, 10, 1, [application])) },
            "/api/trainers/recruitment-status" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new TrainerRecruitmentStatus(true)) },
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        })) { BaseAddress = new("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "manager", "Manager", "manager@example.test", ["Manager"]);
        var storage = new Storage(JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false)));
        var session = new AuthSession(storage, new AuthApiClient(http));
        using var api = new TrainerApiClient(http, session);
        var capture = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(api).AddSingleton<IJSRuntime>(storage)
            .AddSingleton(new ToastService()).AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<TrainerApplications>();
            var open = typeof(TrainerApplications).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<int, Task>>(capture.Page!);
            await ((IHandleEvent)capture.Page!).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() => open(7))), null);
            var html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Coach", html);
            Assert.Equal(eligible, html.Contains("Gửi lại email thiết lập"));
        });
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public TrainerApplications? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is TrainerApplications page) Page = page;
            return component;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }

    private sealed class Storage(string value) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((T)(object)value) : throw new InvalidOperationException(identifier);
    }
}
