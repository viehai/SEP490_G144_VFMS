using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Auth;
using VFMS.Client.Pages.Staff;
using VFMS.Client.Components.Payments;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.PersonalTraining;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Theory]
    [InlineData(false, "/api/staff/membership-purchases/ORD-CASH/confirm-cash")]
    [InlineData(true, "/api/pt/purchases/ORD-CASH/cash-confirmation")]
    public async Task StaffOrderPageShowsOnlyEligibleCashActionsAndUsesTheMatchingReceiptFlow(bool pt, string receiptPath)
    {
        const decimal due = 4_800_000m;
        var now = DateTimeOffset.UtcNow;
        var pending = new StaffPackageOrderResponse(1, "ORD-CASH", "Khách tại quầy", now, now.AddHours(24),
            "Pending", "Pending", "Cash", due, 0m, null, null, null, pt, true,
            [pt ? "PT" : "GYM"], [new(pt ? "PT 12 buổi" : "Gym 3 tháng", due)]);
        var online = new StaffPackageOrderResponse(2, "ORD-QR", "Khách trực tuyến", now, now.AddMinutes(15),
            "Paid", "Paid", "VietQR", 300_000m, 300_000m, now, "INV-QR", null, false, false,
            ["YOGA"], [new("Yoga 1 tháng", 300_000m)]);
        var receipts = 0;
        var listRequests = 0;
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/api/staff/membership-purchases?page=1&pageSize=20", request.RequestUri!.PathAndQuery);
                listRequests++;
                var cash = receipts == 0 ? pending : pending with { Status = "Paid", PaymentStatus = "Paid", CanConfirmCash = false };
                return Json(new PagedResponse<StaffPackageOrderResponse>(1, 20, 2, [cash, online]));
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(receiptPath, request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(due, body.GetProperty("amountReceived").GetDecimal());
            receipts++;
            return pt
                ? Json(new PTCheckoutResponse(1, pending.OrderNumber, "Paid", due, now.UtcDateTime, null, "INV-CASH"))
                : Json(new MembershipPurchaseResponse(pending.OrderNumber, "Paid", "Paid", "Cash", now, now.AddHours(24), now,
                    due, due, [], null, "INV-CASH", null, null));
        })) { BaseAddress = new Uri("https://api.example.test/") };

        var login = new LoginResponse("access-token", "refresh-token", "Bearer", 1800, "staff", "Staff", "staff@example.test", ["Staff"]);
        var storage = new ReadOnlyStorage(JsonSerializer.Serialize(new StoredAuthSession(login, now.AddMinutes(20), false)));
        using var auth = new AuthApiClient(http);
        var session = new AuthSession(storage, auth);
        using var plans = new MembershipPlanApiClient(http, session);
        using var training = new PersonalTrainingApiClient(http, session);
        var activator = new StaffOrderPageActivator();
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(plans).AddSingleton(training).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<MembershipCashReceipt>();
            var html = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Thu tiền đơn ORD-CASH", html);
            Assert.Contains("Xem chi tiết đơn ORD-QR", html);
            Assert.DoesNotContain("Thu tiền đơn ORD-QR", html);
            Assert.Contains("4.800.000 ₫", html);
            Assert.Contains("Trạng thái giao dịch", html);

            var page = activator.Page!;
            var type = typeof(MembershipCashReceipt);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var events = (IHandleEvent)page;
            var open = type.GetMethod("OpenDetails", flags)!.CreateDelegate<Action<StaffPackageOrderResponse>>(page);
            await events.HandleEventAsync(new EventCallbackWorkItem(open), pending);
            var details = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("value=\"4.800.000\"", details);
            Assert.Contains("data-vnd-input", details);
            Assert.DoesNotContain(">Đã nhận</dt>", details);

            var cashForm = activator.CashForm!;
            var confirm = typeof(CashReceiptForm).GetMethod("ConfirmAsync", flags)!.CreateDelegate<Func<Task>>(cashForm);
            var setAmount = typeof(CashReceiptForm).GetMethod("SetCashTendered", flags)!.CreateDelegate<Action<string>>(cashForm);
            Task EnterAmountAsync(string text) => ((IHandleEvent)cashForm).HandleEventAsync(new EventCallbackWorkItem(setAmount), text);

            await EnterAmountAsync("3000");
            Assert.Contains("value=\"3.000\"", view.ToHtmlString());
            await EnterAmountAsync("3.0000");
            Assert.Contains("value=\"30.000\"", view.ToHtmlString());
            await EnterAmountAsync("30.0000");
            Assert.Contains("value=\"300.000\"", view.ToHtmlString());

            await EnterAmountAsync("4.000.000");
            Assert.Contains("Còn thiếu 800.000 ₫", WebUtility.HtmlDecode(view.ToHtmlString()));
            await confirm();
            Assert.Equal(0, receipts);
            await EnterAmountAsync("48.000.00");
            Assert.Contains("Nhập số tiền hợp lệ", WebUtility.HtmlDecode(view.ToHtmlString()));
            await confirm();
            Assert.Equal(0, receipts);

            if (pt)
            {
                await EnterAmountAsync("5.000.000");
                Assert.Contains("Trả lại khách 200.000 ₫", WebUtility.HtmlDecode(view.ToHtmlString()));
                Assert.Contains("value=\"5.000.000\"", view.ToHtmlString());
            }
            else await EnterAmountAsync("4.800.000");

            await confirm();
            Assert.Contains("Hóa đơn INV-CASH", (string)type.GetField("_notice", flags)!.GetValue(page)!);
        });
        Assert.Equal(1, receipts);
        Assert.Equal(2, listRequests);
    }

    private sealed class StaffOrderPageActivator : IComponentActivator
    {
        public MembershipCashReceipt? Page { get; private set; }
        public CashReceiptForm? CashForm { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is MembershipCashReceipt page) Page = page;
            if (component is CashReceiptForm cashForm) CashForm = cashForm;
            return component;
        }
    }
}
