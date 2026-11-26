using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.Lockers;
using VFMS.Client.Components.Payments;
using System.Reflection;
using VFMS.Contracts.Lockers;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class LockerClientTests
{
    [Fact]
    public async Task LockerCashReceiptUsesThePackageCashTenderedForm()
    {
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CollectPayment>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CollectPayment.Order)] = new LockerCheckout(1, "LKO-TEST", "Pending", 690_000,
                    DateTime.UtcNow.AddMinutes(15), null, null, 0, null, null),
                [nameof(CollectPayment.Method)] = "Cash"
            }))).ToHtmlString()));
        Assert.Contains("Khách đưa (₫)", html);
        Assert.Contains("value=\"690.000\"", html);
        Assert.Contains("data-vnd-input=\"whole\"", html);
        Assert.Contains("Xác nhận đã nhận tiền", html);
        Assert.DoesNotContain("Số tiền thực nhận", html);
    }

    [Theory]
    [InlineData("600.000", "Còn thiếu 90.000 ₫", false)]
    [InlineData("690.000", null, true)]
    [InlineData("700.000", "Trả lại khách 10.000 ₫", true)]
    [InlineData("69.00.00", "Nhập số tiền hợp lệ", false)]
    [InlineData("", "Nhập số tiền hợp lệ", false)]
    public async Task SharedCashReceiptNeverBooksTenderedChangeAsRevenue(string tendered, string? feedback, bool canSubmit)
    {
        var activator = new TrackingActivator();
        decimal? recorded = null;
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<CashReceiptForm>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CashReceiptForm.OrderNumber)] = "LKO-TEST",
                [nameof(CashReceiptForm.AmountDue)] = 690_000m,
                [nameof(CashReceiptForm.OnConfirm)] = EventCallback.Factory.Create<decimal>(this, amount => recorded = amount)
            }));
            var form = activator.Instances.OfType<CashReceiptForm>().Single();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var input = typeof(CashReceiptForm).GetMethod("SetCashTendered", flags)!.CreateDelegate<Action<string>>(form);
            var confirm = typeof(CashReceiptForm).GetMethod("ConfirmAsync", flags)!.CreateDelegate<Func<Task>>(form);
            await ((IHandleEvent)form).HandleEventAsync(new EventCallbackWorkItem(input), tendered);
            if (feedback is not null) Assert.Contains(feedback, WebUtility.HtmlDecode(view.ToHtmlString()));
            await confirm();
            Assert.Equal(canSubmit ? 690_000m : (decimal?)null, recorded);
        });
    }

    [Fact]
    public async Task SharedCashReceiptBlocksBusyOrUnverifiedActionsAndResetsForAnotherOrder()
    {
        var activator = new TrackingActivator();
        var receipts = 0;
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<CashReceiptForm>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CashReceiptForm.OrderNumber)] = "LKO-1", [nameof(CashReceiptForm.AmountDue)] = 690_000m,
                [nameof(CashReceiptForm.Blocked)] = true,
                [nameof(CashReceiptForm.OnConfirm)] = EventCallback.Factory.Create<decimal>(this, _ => receipts++)
            }));
            var form = activator.Instances.OfType<CashReceiptForm>().Single();
            var confirm = typeof(CashReceiptForm).GetMethod("ConfirmAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate<Func<Task>>(form);
            await confirm();
            Assert.Equal(0, receipts);
            await form.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CashReceiptForm.Blocked)] = false, [nameof(CashReceiptForm.Busy)] = true }));
            await confirm();
            Assert.Equal(0, receipts);
            await form.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CashReceiptForm.Busy)] = false, [nameof(CashReceiptForm.OrderNumber)] = "LKO-2", [nameof(CashReceiptForm.AmountDue)] = 250_000m }));
            Assert.Contains("value=\"250.000\"", view.ToHtmlString());
            await confirm();
            Assert.Equal(1, receipts);
        });
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("VietQR")]
    public async Task SharedCheckoutOnlyPassesTheChosenMethodAndTrimmedVoucherToItsFeature(string method)
    {
        var activator = new TrackingActivator();
        (string Method, string? Voucher)? chosen = null;
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<CheckoutPaymentOptions>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CheckoutPaymentOptions.OrderNumber)] = "ORDER-1", [nameof(CheckoutPaymentOptions.ShowWallet)] = false,
                [nameof(CheckoutPaymentOptions.OnChoose)] = EventCallback.Factory.Create<(string Method, string? Voucher)>(this, value => chosen = value)
            }));
            var form = activator.Instances.OfType<CheckoutPaymentOptions>().Single();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(CheckoutPaymentOptions).GetField("_voucher", flags)!.SetValue(form, " SAVE20 ");
            var choose = typeof(CheckoutPaymentOptions).GetMethod("ChooseAsync", flags)!.CreateDelegate<Func<string, Task>>(form);
            await choose(method);
            Assert.Equal((method, "SAVE20"), chosen);
            chosen = null;
            await form.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CheckoutPaymentOptions.Busy)] = true }));
            await choose(method);
            Assert.Null(chosen);
        });
    }
}
