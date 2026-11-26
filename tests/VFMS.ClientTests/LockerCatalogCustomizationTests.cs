using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.Lockers;
using VFMS.Client.Pages.Manager.Locker;
using VFMS.Client.Pages.Staff;
using VFMS.Client.Auth;
using VFMS.Contracts.Auth;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Services;
using VFMS.Contracts.Lockers;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class LockerClientTests
{
    [Fact]
    public async Task LockerPaymentChoiceShowsCashVietQrAndVoucherWithoutAStaffConsentCheckbox()
    {
        var order = new LockerCheckout(7, "LKO-7", "Pending", 250_000, DateTime.UtcNow.AddMinutes(15), null, null, 0, null, null, CanChoosePayment: true);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(Client(HttpStatusCode.OK, order))
            .AddSingleton<ToastService>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerPaymentChoice>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(LockerPaymentChoice.Order)] = order, [nameof(LockerPaymentChoice.Staff)] = true }))).ToHtmlString()));
        Assert.Contains("Tạo mã VietQR", html);
        Assert.Contains("Thanh toán tiền mặt tại quầy", html);
        Assert.Contains("Voucher", html);
        Assert.DoesNotContain("type=\"checkbox\"", html);
    }

    [Fact]
    public async Task LockerActionErrorsAreAnnouncedAsErrorToastsAlongsideSuccessMessages()
    {
        var toasts = new ToastService();
        toasts.Show("Đã tạo khu.");
        toasts.ShowError("Không thể tạo khu.");
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(toasts).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<VFMS.Client.Components.UI.ToastHost>()).ToHtmlString()));
        Assert.Contains("Đã tạo khu.", html);
        Assert.Contains("Không thể tạo khu.", html);
        Assert.Contains("toast-item--error", html);
        Assert.Contains("role=\"alert\"", html);
    }

    [Fact]
    public async Task LockerContractDetailsKeepsTheOpenPaymentPanelDuringRefresh()
    {
        var rental = new LockerRentalView(7, "LK-07", new(1, "A01", "A", 0, "Occupied"), "Monthly", "Active",
            250_000, 100_000, null, null, null, 0, 0, null, true, null, null, null, false, 1, null, 0, null);
        var item = new LockerStaffRental(rental, "member", "Nguyễn An", "0901234567");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/pricing")
            ? Task.FromResult(Response(new[] { new LockerPolicy(1, "1 tháng", "Monthly", 1, 250_000, 100_000, 10_000) }))
            : ++reads == 1 ? Task.FromResult(Response(item)) : pending.Task)) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "staff", "Staff", "staff@example.test", ["Staff"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        var api = new LockerApiClient(new VfmsApiClient(http, new AuthSession(new Storage(stored), new AuthApiClient(http))));
        var activator = new TrackingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(api).AddSingleton<ToastService>()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<LockerContractDetails>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(LockerContractDetails.Id)] = 7 }));
            var panel = Assert.Single(activator.Instances.OfType<LockerContractPanel>());
            var refresh = panel.OnChanged.InvokeAsync();
            try { Assert.Contains("LK-07", WebUtility.HtmlDecode(root.ToHtmlString())); }
            finally { pending.TrySetResult(Response(item)); }
            await refresh;
            Assert.Same(panel, Assert.Single(activator.Instances.OfType<LockerContractPanel>()));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LockerCatalogOnlyOffersZoneDeletionWhenServerConfirmsItIsEmpty(bool canDelete)
    {
        var catalog = new LockerCatalog([new(1, "C", 0, [], canDelete)], [], "");
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(Client(HttpStatusCode.OK, catalog))
            .AddSingleton<ToastService>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerCatalogPage>()).ToHtmlString()));
        Assert.Equal(canDelete, html.Contains("Xóa khu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LockerMaintenanceRetainsItsCellAndDoesNotDisplayFloor()
    {
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LockerGrid.Slots)] = new[] { new LockerSlot(1, "A01", "A", 99, "Maintenance") }
            }))).ToHtmlString());
        html = WebUtility.HtmlDecode(html);
        Assert.Contains("locker-cell", html);
        Assert.Contains("Bảo trì", html);
        Assert.DoesNotContain("Tầng", html);
        Assert.DoesNotContain("tầng", html);
        ExportDemo("grid", html);
    }

    [Fact]
    public async Task LockerRulesPanelShowsTheActualDepositAndRequestedRules()
    {
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerRulesPanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LockerRulesPanel.KeyDeposit)] = 125_000m
            }))).ToHtmlString());
        html = WebUtility.HtmlDecode(html);
        Assert.Contains("125.000", html);
        Assert.Contains("Giữ gìn chìa khóa.", html);
        Assert.Contains("Không cất đồ dễ cháy hoặc tài sản có giá trị.", html);
        Assert.Contains("Thuê ngày trả chìa trước 22:00.", html);
        Assert.Contains("Mỗi hội viên tối đa một tủ.", html);
        ExportDemo("rules", html);
    }

    [Fact]
    public async Task LockerPricingPageRendersCustomMonthlyRowsOnItsOwnPage()
    {
        var api = Client(HttpStatusCode.OK, new LockerCatalog([], [
            new(1, "2 tháng", "Monthly", 2, 430_000, 100_000, 10_000),
            new(2, "5 tháng", "Monthly", 5, 1_000_000, 100_000, 10_000)
        ], ""));
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(api)
            .AddSingleton<ToastService>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerPricingPage>()).ToHtmlString());
        html = WebUtility.HtmlDecode(html);
        Assert.Contains("Bảng giá thuê tủ", html);
        Assert.Contains("Thêm mức giá", html);
        Assert.Contains("value=\"2\"", html);
        Assert.Contains("value=\"5\"", html);
        Assert.DoesNotContain("Thêm khu", html);
        ExportDemo("pricing", html);
    }

    [Fact]
    public async Task LockerCatalogHasSeparateFormsAndOnlyTheSelectedZoneDiagram()
    {
        var catalog = new LockerCatalog([
            new(1, "A", 1, [new(1, "A01", "A", 1, "Available"), new(2, "A02", "A", 1, "Maintenance")]),
            new(2, "B", 2, [new(3, "B01", "B", 2, "Available")])
        ], [new(1, "1 tháng", "Monthly", 1, 250_000, 100_000, 10_000)], "");
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(Client(HttpStatusCode.OK, catalog))
            .AddSingleton<ToastService>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerCatalogPage>()).ToHtmlString());
        html = WebUtility.HtmlDecode(html);
        Assert.Contains("Thêm khu tự động", html);
        Assert.Contains("Khu thêm tủ", html);
        Assert.Contains("Sơ đồ Khu A", html);
        Assert.Contains("A02", html);
        Assert.DoesNotContain("B01", html);
        Assert.DoesNotContain("Tầng", html);
        Assert.Contains("/manager/locker-pricing", html);
        Assert.Contains("locker-rules", html);
        ExportDemo("catalog", html);
    }

    // Optional export uses the exact rendered Razor markup for this task's inline preview.
    private static void ExportDemo(string name, string html)
    {
        var directory = Environment.GetEnvironmentVariable("VFMS_LOCKER_DEMO_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".fragment.html"), html);
    }
}
