using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Components.Lockers;
using VFMS.Client.Components.Workspace;
using VFMS.Client.Services;
using VFMS.Contracts.Lockers;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class LockerClientTests
{
    [Fact]
    public async Task LockerKeyLossRequiresConfirmationBeforeRecordingReturnAndCancelWritesNothing()
    {
        var activator = new TrackingActivator();
        var rental = new LockerRentalView(7, "LK-07", new(1, "A01", "A", 0, "Occupied"), "Monthly", "Active",
            250_000, 100_000, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(29), null, 0, 0, null, true,
            null, null, null, false, 1, null, 0, null, KeyDeposit: 100_000);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(Client(HttpStatusCode.OK,
            new LockerSettlementQuote(0, 100_000, 0, 0, 100_000, DateTime.UtcNow)))
            .AddSingleton<ToastService>().AddSingleton<IJSRuntime>(new Storage(""))
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<LockerContractPanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LockerContractPanel.Item)] = new LockerStaffRental(rental, "member", "Nguyễn An", "0901234567"),
                [nameof(LockerContractPanel.Policies)] = Array.Empty<LockerPolicy>()
            }));
            var panel = activator.Instances.OfType<LockerContractPanel>().Single();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(LockerContractPanel).GetField("_lockerEmpty", flags)!.SetValue(panel, true);
            typeof(LockerContractPanel).GetField("_keyLost", flags)!.SetValue(panel, true);
            var record = typeof(LockerContractPanel).GetMethod("RecordReturnAsync", flags)!.CreateDelegate<Func<Task>>(panel);
            await ((IHandleEvent)panel).HandleEventAsync(new EventCallbackWorkItem(record), null);
            Assert.Empty(_requests);
            Assert.Contains("Xác nhận mất cọc", WebUtility.HtmlDecode(view.ToHtmlString()));
            Assert.Contains("100.000 ₫", WebUtility.HtmlDecode(view.ToHtmlString()));
            var modal = activator.Instances.OfType<WorkspaceModal>().Single();
            await modal.OnClose.InvokeAsync();
            Assert.Empty(_requests);
            Assert.DoesNotContain("role=\"dialog\"", view.ToHtmlString());
            await ((IHandleEvent)panel).HandleEventAsync(new EventCallbackWorkItem(record), null);
            var confirm = typeof(LockerContractPanel).GetMethod("ConfirmKeyLossReturnAsync", flags)!.CreateDelegate<Func<Task>>(panel);
            await ((IHandleEvent)panel).HandleEventAsync(new EventCallbackWorkItem(confirm), null);
            Assert.Equal(HttpMethod.Post, Assert.Single(_requests).Method);
            Assert.EndsWith("/staff/rentals/7/return", _requests[0].RequestUri!.AbsolutePath);
            await confirm();
            Assert.Single(_requests);
        });
    }
}
