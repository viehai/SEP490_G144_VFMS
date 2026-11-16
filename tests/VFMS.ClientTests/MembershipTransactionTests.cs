using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Layout;
using VFMS.Client.Pages.Customer;
using VFMS.Client.Components.Memberships;
using VFMS.Client.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task TransactionsApi_UsesAuthenticatedPagedGetWithoutUserId(string role)
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-transactions?page=2&pageSize=10", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(new PagedResponse<MembershipTransactionSummaryResponse>(2, 10, 11, [Transaction()])));
        }, role: role);

        var page = await api.ListTransactionsAsync(2);

        Assert.Equal(11, page.TotalCount);
        Assert.Equal(7, Assert.Single(page.Items).OrderId);
    }

    [Fact]
    public async Task TransactionApi_ReadsDetailSnapshotsAndInvoiceWithoutMutation()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/membership-transactions/7", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(TransactionDetailData()));
        }, role: "Customer");

        var detail = await api.GetTransactionAsync(7);

        Assert.Equal(500_000m, detail.Subtotal);
        Assert.Equal("ORIGINAL20", detail.Voucher?.Code);
        Assert.Equal("Original gym package", Assert.Single(detail.Items).PlanName);
        Assert.Equal("INV-ORIGINAL", detail.Invoice?.InvoiceNumber);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task TransactionApi_PreservesAuthorizationAndNotFoundResponses(int status)
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new { code = "TRANSACTION_NOT_FOUND" }, (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.GetTransactionAsync(7));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
    }

    [Fact]
    public async Task TransactionsApi_AnonymousSessionDoesNotSendRequest()
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Unexpected request."), authenticated: false);
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.ListTransactionsAsync(1));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Theory]
    [InlineData(typeof(MyTransactions))]
    [InlineData(typeof(TransactionDetail))]
    public async Task TransactionPages_AuthorizeCustomerAndMemberAndDenyOtherRoles(Type page)
    {
        using var services = new ServiceCollection().AddLogging().AddAuthorizationCore().BuildServiceProvider();
        var metadata = page.GetCustomAttributes<AuthorizeAttribute>().ToArray();
        var policy = await AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(), metadata);
        Assert.NotNull(policy);
        foreach (var role in new[] { "Customer", "Member", "Manager", "Staff", "Admin", "PT" })
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
            var allowed = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy!);
            Assert.Equal(role is "Customer" or "Member", allowed.Succeeded);
        }
        Assert.False((await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(new ClaimsPrincipal(), null, policy!)).Succeeded);
    }

    [Theory]
    [InlineData("Customer", "/my-transactions")]
    [InlineData("Member", "/my-transactions/7")]
    public async Task TransactionReturnUrl_PreservesAuthorizedLandingWithoutChangingRoleDefault(string role, string path)
    {
        using var services = new ServiceCollection().AddLogging().AddAuthorizationCore().BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
        Assert.Equal("/", AuthNavigation.DefaultRoute(principal));
        Assert.Equal(path, await AuthNavigation.LoginDestinationAsync(principal, path,
            services.GetRequiredService<IAuthorizationPolicyProvider>(), services.GetRequiredService<IAuthorizationService>()));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task TransactionHistory_RendersOwnedRecordsAndResponsiveLinks(string role)
    {
        var transaction = Transaction() with
        {
            OrderNumber = "ORD-20261115-AVERYLONGORDERREFERENCE1234567890",
            InvoiceNumber = "INV-20261115-AVERYLONGINVOICEREFERENCE1234567890"
        };
        var html = await RenderTransactionPageAsync<MyTransactions>(
            new PagedResponse<MembershipTransactionSummaryResponse>(1, 10, 11, [transaction]), role: role);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Lịch sử giao dịch", decoded);
        Assert.Contains(transaction.OrderNumber, decoded);
        Assert.Contains(transaction.InvoiceNumber, decoded);
        Assert.Contains("400.000 ₫", decoded);
        Assert.Contains("VietQR / SePay", decoded);
        Assert.Contains("Đã thanh toán", decoded);
        Assert.Contains("href=\"/my-transactions/7\"", html);
        Assert.Contains("Phân trang lịch sử giao dịch", decoded);
        Assert.Contains("Trang 1 / 2", decoded);
        Assert.Contains("break-all", html);
        Assert.DoesNotContain("<table", html);

        if (PreviewDirectory is { } output && role == "Member")
            await WriteTransactionPreviewAsync(html, output, "history-populated.html");
    }

    [Fact]
    public async Task TransactionHistory_EmptyIsStandardMsg01State()
    {
        var html = await RenderTransactionPageAsync<MyTransactions>(new PagedResponse<MembershipTransactionSummaryResponse>(1, 10, 0, []));
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("MSG-01", decoded);
        Assert.Contains("Khám phá gói hội viên", decoded);
        Assert.DoesNotContain("role=\"alert\"", html);
        if (PreviewDirectory is { } output)
            await WriteTransactionPreviewAsync(html, output, "history-empty.html");
    }

    [Theory]
    [InlineData("Pending", "Failed", "Thất bại")]
    [InlineData("Paid", "Refunded", "Đã hoàn tiền")]
    public async Task TransactionHistory_ShowsPaymentResultSeparatelyFromOrderStatus(string orderStatus, string paymentStatus, string expected)
    {
        var transaction = Transaction() with { OrderStatus = orderStatus, PaymentStatus = paymentStatus, ItemCount = 2 };
        var html = WebUtility.HtmlDecode(await RenderTransactionPageAsync<MyTransactions>(
            new PagedResponse<MembershipTransactionSummaryResponse>(1, 10, 1, [transaction])));
        Assert.Contains(expected, html);
        Assert.Contains("Thanh toán", html);
        Assert.Contains("+1 gói khác", html);
    }

    [Theory]
    [InlineData("Customer", true)]
    [InlineData("Member", true)]
    [InlineData("Manager", false)]
    [InlineData("Staff", false)]
    [InlineData("Admin", false)]
    [InlineData("PT", false)]
    public async Task PublicNavigation_ShowsTransactionHistoryOnlyForCustomerOrMember(string role, bool visible)
    {
        var html = await RenderTransactionLayoutAsync("<p>Portal</p>", role);
        Assert.Equal(visible, html.Contains("href=\"/my-transactions\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(503, "Thử lại")]
    [InlineData(401, "Đăng nhập lại")]
    public async Task TransactionHistory_ErrorAndSessionExpiryShowActionableSafeMessages(int status, string action)
    {
        var html = await RenderTransactionPageAsync<MyTransactions>(new { detail = "Internal stack trace MUST NOT leak" }, (HttpStatusCode)status);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains(action, decoded);
        Assert.DoesNotContain("Internal stack", decoded);
        if (status == 401)
            Assert.Contains("MSG-11", decoded);
        if (PreviewDirectory is { } output && status == 503)
            await WriteTransactionPreviewAsync(html, output, "history-error.html");
    }

    [Fact]
    public async Task TransactionHistory_LoadingAppearsUntilRequestCompletes()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var api = CreateClient(_ => response.Task, role: "Customer");
        await using var services = TransactionPageServices(api);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<MyTransactions>());
        var loading = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("Đang tải lịch sử giao dịch", WebUtility.HtmlDecode(loading));
        Assert.Contains("aria-busy=\"true\"", loading);
        if (PreviewDirectory is { } output)
            await WriteTransactionPreviewAsync(loading, output, "history-loading.html");
        response.SetResult(Json(new PagedResponse<MembershipTransactionSummaryResponse>(1, 10, 0, [])));
        await rendered.QuiescenceTask;
        Assert.Contains("MSG-01", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
    }

    [Fact]
    public async Task TransactionDetail_RendersOriginalItemsFinancialsVoucherAndInvoice()
    {
        var detail = TransactionDetailData();
        var html = await RenderTransactionPageAsync<TransactionDetail>(detail, parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 });
        var decoded = WebUtility.HtmlDecode(html);
        foreach (var value in new[] { "Original gym package", "500.000 ₫", "100.000 ₫", "400.000 ₫", "ORIGINAL20", "Original campaign", "INV-ORIGINAL", "Đã phát hành", "Dành cho bạn", "Ngày kích hoạt đã chọn" })
            Assert.Contains(value, decoded);
        Assert.Contains("Tạm tính", decoded);
        Assert.Contains("Số tiền đã nhận", decoded);
        Assert.Contains("Chi tiết hóa đơn", decoded);
        if (PreviewDirectory is { } output)
        {
            detail = detail with
            {
                Transaction = detail.Transaction with { OrderNumber = "ORD-20261115-AVERYLONGORDERREFERENCE1234567890" },
                Invoice = detail.Invoice! with { InvoiceNumber = "INV-20261115-AVERYLONGINVOICEREFERENCE1234567890" },
                Voucher = detail.Voucher! with { Code = "NOVEMBERAVERYLONGVOUCHERCODE1234567890", Name = "Chương trình ưu đãi hội viên với tên chiến dịch dài trên thiết bị di động" },
                Items = [detail.Items[0] with { PlanName = "Gói hội viên toàn diện với tên gói dài để kiểm tra hiển thị trên thiết bị di động" }]
            };
            await WriteTransactionPreviewAsync(await RenderTransactionPageAsync<TransactionDetail>(detail,
                parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 }), output, "transaction-detail.html");
        }
    }

    [Theory]
    [InlineData("Cash", "Paid", "Paid", "Tiền mặt tại quầy")]
    [InlineData("VietQR", "Expired", "Cancelled", "Hết hạn")]
    public async Task TransactionDetail_HandlesActualPaymentMethodsLifecycleAndNoInvoice(string method, string orderStatus, string paymentStatus, string expected)
    {
        var detail = TransactionDetailData() with { Invoice = null, Voucher = null };
        detail = detail with { Transaction = detail.Transaction with { PaymentMethod = method, OrderStatus = orderStatus, PaymentStatus = paymentStatus, IsRenewal = true, InvoiceNumber = null } };
        var decoded = WebUtility.HtmlDecode(await RenderTransactionPageAsync<TransactionDetail>(detail,
            parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 }));
        Assert.Contains(expected, decoded);
        Assert.Contains("Gia hạn gói hội viên", decoded);
        Assert.Contains("Giao dịch này chưa có hóa đơn", decoded);
        Assert.DoesNotContain("Mã giảm giá của giao dịch", decoded);
    }

    [Fact]
    public async Task TransactionDetail_NoChargeUsesZeroAmountsAndDoesNotClaimAnExternalPayment()
    {
        var detail = TransactionDetailData();
        detail = detail with
        {
            Transaction = detail.Transaction with { PaymentMethod = "NoCharge", AmountDue = 0, AmountReceived = 0 },
            DiscountAmount = 500_000m,
            Voucher = detail.Voucher! with { DiscountAmount = 500_000m }
        };
        var decoded = WebUtility.HtmlDecode(await RenderTransactionPageAsync<TransactionDetail>(detail,
            parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 }));
        Assert.Contains("Không thu tiền", decoded);
        Assert.Contains("không phát sinh chuyển khoản", decoded);
        Assert.Contains("0 ₫", decoded);
        Assert.DoesNotContain("VietQR / SePay", decoded);
    }

    [Fact]
    public async Task TransactionDetail_NotFoundDoesNotExposeAnOrder()
    {
        var html = await RenderTransactionPageAsync<TransactionDetail>(new { code = "TRANSACTION_NOT_FOUND" }, HttpStatusCode.NotFound,
            parameters: new() { [nameof(TransactionDetail.OrderId)] = 999 });
        Assert.Contains("Không tìm thấy giao dịch", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("INV-ORIGINAL", html);
    }

    private static MembershipTransactionSummaryResponse Transaction() => new(7, "ORD-ORIGINAL",
        new DateTimeOffset(2026, 11, 15, 3, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 15, 3, 2, 0, TimeSpan.Zero),
        "Paid", "Paid", "VietQR", false, "Original gym package", 1, 400_000m, 400_000m, "INV-ORIGINAL");

    private static MembershipTransactionDetailResponse TransactionDetailData() => new(Transaction(), 500_000m, 100_000m,
        new DateTimeOffset(2026, 11, 15, 3, 2, 0, TimeSpan.Zero),
        [new("Original gym package", 500_000m, true, new DateOnly(2026, 11, 16), false)],
        new("ORIGINAL20", "Original campaign", 100_000m, "Confirmed"),
        new("INV-ORIGINAL", new DateTimeOffset(2026, 11, 15, 3, 2, 0, TimeSpan.Zero), "Issued"));

    private static string? PreviewDirectory => Environment.GetEnvironmentVariable("VFMS_UC20_PREVIEW_DIR");

    private static ServiceProvider TransactionPageServices(VFMS.Client.Services.MembershipPlanApiClient api) =>
        new ServiceCollection().AddLogging().AddSingleton(api).AddSingleton<IJSRuntime>(new ReadOnlyStorage(null)).BuildServiceProvider();

    private static async Task<string> RenderTransactionPageAsync<TComponent>(object response, HttpStatusCode status = HttpStatusCode.OK,
        string role = "Customer", Dictionary<string, object?>? parameters = null) where TComponent : IComponent
    {
        using var api = CreateClient(_ => Task.FromResult(Json(response, status)), role: role);
        await using var provider = TransactionPageServices(api);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters ?? []))).ToHtmlString());
    }

    private static async Task WriteTransactionPreviewAsync(string html, string output, string fileName)
    {
        Directory.CreateDirectory(output);
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var wwwroot = Path.Combine(workspace, "src/VFMS.Client/wwwroot");
        var index = await File.ReadAllTextAsync(Path.Combine(wwwroot, "index.html"));
        var head = index[(index.IndexOf("<head>", StringComparison.Ordinal) + 6)..index.IndexOf("</head>", StringComparison.Ordinal)]
            .Replace("<base href=\"/\" />", "");
        var tailwindStart = index.IndexOf("<script src=\"https://cdn.tailwindcss.com\"", StringComparison.Ordinal);
        var tailwindEnd = index.IndexOf("</script>", index.IndexOf("</script>", tailwindStart, StringComparison.Ordinal) + 9, StringComparison.Ordinal) + 9;
        var tailwind = index[tailwindStart..tailwindEnd];
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        foreach (Match link in Regex.Matches(head, "href=\"(?<path>[^\"]+\\.css(?:\\?[^\"]*)?)\""))
        {
            var path = link.Groups["path"].Value.Split('?')[0];
            if (Uri.TryCreate(path, UriKind.Absolute, out _))
                continue;
            var source = path == "VFMS.Client.styles.css"
                ? Path.Combine(workspace, "src/VFMS.Client/obj", configuration, "net8.0/scopedcss/bundle", path)
                : Path.Combine(wwwroot, path);
            var destination = Path.Combine(output, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, await File.ReadAllTextAsync(source));
        }
        var layout = await RenderTransactionLayoutAsync(html, "Member");
        await File.WriteAllTextAsync(Path.Combine(output, fileName), $"<!doctype html><html lang=\"vi\"><head>{head}</head><body class=\"bg-vfms-bg text-vfms-text\">{layout}{tailwind}</body></html>");
    }

    private static async Task<string> RenderTransactionLayoutAsync(string html, string role)
    {
        var storage = new ReadOnlyStorage(null);
        using var http = new HttpClient();
        var session = new AuthSession(storage, new AuthApiClient(http));
        await using var services = new ServiceCollection().AddLogging().AddAuthorizationCore()
            .AddSingleton<IJSRuntime>(storage).AddSingleton(session)
            .AddSingleton<NavigationManager>(new TransactionPreviewNavigation()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var identity = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "Hội viên")], "Test"));
        RenderFragment body = builder => builder.AddMarkupContent(0, html);
        RenderFragment layout = builder =>
        {
            builder.OpenComponent<PublicLayout>(0);
            builder.AddAttribute(1, nameof(PublicLayout.Body), body);
            builder.CloseComponent();
        };
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CascadingValue<Task<AuthenticationState>>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CascadingValue<Task<AuthenticationState>>.Value)] = Task.FromResult(new AuthenticationState(identity)),
                [nameof(CascadingValue<Task<AuthenticationState>>.ChildContent)] = layout
            }))).ToHtmlString());
    }

    private sealed class TransactionPreviewNavigation : NavigationManager
    {
        public TransactionPreviewNavigation() => Initialize("https://client.example.test/", "https://client.example.test/my-transactions");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected preview navigation.");
    }
}
