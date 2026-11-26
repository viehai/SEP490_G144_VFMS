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
using VFMS.Client.Components.PersonalTraining;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using Xunit;

namespace VFMS.ClientTests;

public sealed class PTFreezeSlotPickerTests
{
    [Fact]
    public async Task FreezeRequestShowsTheAssignedTrainersScheduleWithoutATrainerTransferControl()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var contract = new PTContractResponse(7, "trainer", "member", "Gói PT", "Active", today,
            today.AddDays(30), 12, 0, 12, 0, 12, 7, 15, 15, [], TrainerName: "Huấn luyện viên hiện tại");
        var login = new LoginResponse("access", "refresh", "Bearer", 1800,
            "member", "Member", "member@example.test", ["Member"]);
        using var http = new HttpClient(new Handler(request => request.RequestUri!.PathAndQuery switch
        {
            "/api/pt/contracts/7/freezes?page=1" => Json(new PagedResponse<PTFreezeResponse>(1, 10, 0, [])),
            "/api/pt/trainers/trainer/weekly-template" => Json(new[] { new PTWeeklySlot(11, DayOfWeek.Monday, new(9, 0), new(10, 0)) }),
            var path => throw new InvalidOperationException(path)
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(new Storage(JsonSerializer.Serialize(
            new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false))), new AuthApiClient(http));
        using var api = new PersonalTrainingApiClient(http, session);
        var activator = new Activator();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(api)
            .AddSingleton(new ToastService()).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<MemberFreezePanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(MemberFreezePanel.Contract)] = contract
            }));
            var panel = activator.Panel!;
            // Open the same form as the "Xin bảo lưu" click; HtmlRenderer does not expose browser DOM events.
            await ((IHandleEvent)panel).HandleEventAsync(new EventCallbackWorkItem((Action)(() =>
                typeof(MemberFreezePanel).GetField("_showRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true))), null);
            var html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Ngày bắt đầu bảo lưu", html);
            Assert.Contains("Xem lịch sau bảo lưu", html);
            Assert.Contains("Chọn ca tập sau bảo lưu", html);
            Assert.Contains("09:00", html);
            Assert.DoesNotContain("Đổi huấn luyện viên", html);
            Assert.DoesNotContain("type=\"checkbox\"", html);
        });
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
    private sealed class Activator : IComponentActivator
    {
        public MemberFreezePanel? Panel { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)System.Activator.CreateInstance(type)!;
            if (component is MemberFreezePanel panel) Panel = panel;
            return component;
        }
    }
    private sealed class Storage(string value) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((T)(object)value) : throw new InvalidOperationException(identifier);
    }
}
