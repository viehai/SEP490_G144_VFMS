using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Components.Memberships;
using VFMS.Client.Pages.Customer;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task InvoicePdfApi_UsesBodylessAuthenticatedBinaryGetWithoutFinancialParameters(string role)
    {
        var expected = "%PDF-1.7\nInvoice binary\n%%EOF"u8.ToArray();
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-transactions/7/invoice/pdf", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(InvoicePdfResponse(expected));
        }, role: role);

        var result = await api.DownloadInvoicePdfAsync(7);

        Assert.Equal(expected, result.Content);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("VFMS-Invoice-INV-ORIGINAL.pdf", result.FileName);
    }

    [Theory]
    [InlineData("../../evil.pdf")]
    [InlineData("..\\evil.pdf")]
    [InlineData("invoice.html")]
    [InlineData("")]
    public async Task InvoicePdfApi_UsesSafeFallbackForInvalidServerFilename(string fileName)
    {
        using var api = CreateClient(_ => Task.FromResult(InvoicePdfResponse(fileName: fileName)));
        Assert.Equal("VFMS-Invoice-7.pdf", (await api.DownloadInvoicePdfAsync(7)).FileName);
    }

    [Theory]
    [InlineData("text/html", "%PDF-1.7\n%%EOF")]
    [InlineData("application/pdf", "<html>server error</html>")]
    [InlineData("application/pdf", "")]
    public async Task InvoicePdfApi_RejectsWrongMimeOrNonPdfContent(string mime, string body)
    {
        using var api = CreateClient(_ => Task.FromResult(InvoicePdfResponse(System.Text.Encoding.UTF8.GetBytes(body), mime: mime)));
        var error = await Assert.ThrowsAsync<VFMS.Client.Services.AuthApiException>(() => api.DownloadInvoicePdfAsync(7));
        Assert.Contains("PDF hợp lệ", error.Message);
        Assert.DoesNotContain("<html>", error.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task InvoicePdfApi_PreservesServerAuthorizationEligibilityAndFailureStatuses(int status)
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new { code = "INVOICE_EXPORT_UNAVAILABLE" }, (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<VFMS.Client.Services.AuthApiException>(() => api.DownloadInvoicePdfAsync(7));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
    }

    [Fact]
    public async Task InvoicePdfApi_AnonymousDoesNotSendRequest()
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Unexpected request."), authenticated: false);
        var error = await Assert.ThrowsAsync<VFMS.Client.Services.AuthApiException>(() => api.DownloadInvoicePdfAsync(7));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task InvoiceDownload_SuccessPassesActualPdfStreamAndSafeFilenameToBrowser()
    {
        var expected = "%PDF-1.7\nAuthoritative invoice\n%%EOF"u8.ToArray();
        using var api = CreateClient(_ => Task.FromResult(InvoicePdfResponse(expected)), role: "Customer");
        var browser = new InvoiceBrowserRuntime();
        await using var harness = await InvoiceDownloadHarness.CreateAsync(api, browser, canExport: true);

        await harness.ClickAsync();

        var download = Assert.Single(browser.Downloads);
        Assert.Equal("VFMS-Invoice-INV-ORIGINAL.pdf", download.FileName);
        Assert.Equal("application/pdf", download.ContentType);
        Assert.Equal(expected, download.Content);
        var html = WebUtility.HtmlDecode(await harness.HtmlAsync());
        Assert.Contains("Đã gửi hóa đơn PDF tới trình duyệt", html);
        Assert.DoesNotContain("role=\"alert\"", html);
    }

    [Fact]
    public async Task InvoiceDownload_InFlightDisablesRepeatedClicksAndRetainsTransactionDetail()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var api = CreateClient(_ => { requests++; return response.Task; });
        var browser = new InvoiceBrowserRuntime();
        await using var harness = await InvoiceDownloadHarness.CreateAsync(api, browser, canExport: true);

        var firstClick = await harness.StartClickAsync();
        var loading = await harness.HtmlAsync();
        Assert.Contains("aria-busy=\"true\"", loading);
        Assert.True(InvoiceButtonDisabled(loading));
        Assert.Contains("Đang tải hóa đơn", WebUtility.HtmlDecode(loading));
        await harness.ClickAsync();
        Assert.Equal(1, requests);
        if (InvoicePreviewDirectory is { } output)
            await WriteInvoicePagePreviewAsync(loading, output, "invoice-downloading.html");
        response.SetResult(InvoicePdfResponse());
        await firstClick;
        Assert.Single(browser.Downloads);
        Assert.Contains("Tải hóa đơn (PDF)", WebUtility.HtmlDecode(await harness.HtmlAsync()));
    }

    [Fact]
    public async Task InvoiceDownload_DisabledServerEligibilityPreventsRequestEvenWhenHandlerInvoked()
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Ineligible invoice requested."));
        await using var harness = await InvoiceDownloadHarness.CreateAsync(api, new InvoiceBrowserRuntime(), canExport: false);
        await harness.ClickAsync();
        var html = WebUtility.HtmlDecode(await harness.HtmlAsync());
        Assert.True(InvoiceButtonDisabled(html));
        Assert.Contains("Chỉ có thể tải hóa đơn đã phát hành", html);
        Assert.DoesNotContain("role=\"alert\"", html);
    }

    [Theory]
    [InlineData(401, "MSG-11")]
    [InlineData(403, "không có quyền")]
    [InlineData(404, "Không tìm thấy hóa đơn")]
    [InlineData(409, "chưa đủ điều kiện")]
    [InlineData(503, "tạm thời chưa sẵn sàng")]
    public async Task InvoiceDownload_HandlesSessionOwnershipEligibilityAndServerErrorsWithoutBreakingPage(int status, string expected)
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new { code = "INVOICE_EXPORT_UNAVAILABLE", detail = "Private database stack trace" }, (HttpStatusCode)status)));
        var browser = new InvoiceBrowserRuntime();
        await using var harness = await InvoiceDownloadHarness.CreateAsync(api, browser, canExport: true);

        await harness.ClickAsync();

        var html = WebUtility.HtmlDecode(await harness.HtmlAsync());
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains(expected, html);
        Assert.DoesNotContain("Private database", html);
        Assert.Empty(browser.Downloads);
        if (status == 401)
        {
            Assert.Contains("Đăng nhập lại", html);
            Assert.Contains("returnUrl=%2Fmy-transactions%2F7", html);
            Assert.True(InvoiceButtonDisabled(html));
        }
        if (InvoicePreviewDirectory is { } output && status == 409)
            await WriteInvoicePagePreviewAsync(html, output, "invoice-error.html");
    }

    [Fact]
    public async Task InvoiceDownload_BrowserFailureAllowsRetryAndShowsSafeMessage()
    {
        using var api = CreateClient(_ => Task.FromResult(InvoicePdfResponse()));
        var browser = new InvoiceBrowserRuntime { Fail = true };
        await using var harness = await InvoiceDownloadHarness.CreateAsync(api, browser, canExport: true);
        await harness.ClickAsync();
        var html = WebUtility.HtmlDecode(await harness.HtmlAsync());
        Assert.Contains("Không tải được tệp PDF trong trình duyệt", html);
        Assert.DoesNotContain("Synthetic browser error", html);
        browser.Fail = false;
        await harness.ClickAsync();
        Assert.Single(browser.Downloads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvoiceDetail_DownloadAvailabilityComesFromServerContract(bool canExport)
    {
        var detail = TransactionDetailData() with { CanExportInvoicePdf = canExport };
        var html = await RenderTransactionPageAsync<TransactionDetail>(detail,
            parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 });
        Assert.Contains("Tải hóa đơn (PDF)", WebUtility.HtmlDecode(html));
        Assert.Equal(!canExport, InvoiceButtonDisabled(html));
        if (InvoicePreviewDirectory is { } output)
        {
            detail = detail with { Invoice = detail.Invoice! with { InvoiceNumber = "INV-20261115-AVERYLONGINVOICEREFERENCE1234567890" } };
            if (!canExport)
                detail = detail with { Transaction = detail.Transaction with { OrderStatus = "Pending", PaymentStatus = "Pending", CompletedAt = null }, Invoice = null, PaidAt = null };
            await WriteTransactionPreviewAsync(await RenderTransactionPageAsync<TransactionDetail>(detail,
                parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 }), output,
                canExport ? "invoice-eligible.html" : "invoice-pending.html");
        }
    }

    private static string? InvoicePreviewDirectory => Environment.GetEnvironmentVariable("VFMS_UC21_PREVIEW_DIR");

    private static bool InvoiceButtonDisabled(string html)
    {
        var button = Assert.Single(Regex.Matches(html, "<button\\b[^>]*>").Cast<Match>());
        return Regex.IsMatch(button.Value, "\\sdisabled(?:\\s|=|>)");
    }

    private static HttpResponseMessage InvoicePdfResponse(byte[]? content = null, string mime = "application/pdf", string fileName = "VFMS-Invoice-INV-ORIGINAL.pdf")
    {
        var body = new ByteArrayContent(content ?? "%PDF-1.7\nInvoice\n%%EOF"u8.ToArray());
        body.Headers.ContentType = new MediaTypeHeaderValue(mime);
        body.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };
        return new(HttpStatusCode.OK) { Content = body };
    }

    private static async Task WriteInvoicePagePreviewAsync(string downloadHtml, string output, string fileName)
    {
        var detail = TransactionDetailData() with
        {
            CanExportInvoicePdf = true,
            Invoice = TransactionDetailData().Invoice! with { InvoiceNumber = "INV-20261115-AVERYLONGINVOICEREFERENCE1234567890" }
        };
        var page = await RenderTransactionPageAsync<TransactionDetail>(detail,
            parameters: new() { [nameof(TransactionDetail.OrderId)] = 7 });
        using var api = CreateClient(_ => throw new InvalidOperationException("Unexpected preview download."));
        await using var harness = await InvoiceDownloadHarness.CreateAsync(api, new InvoiceBrowserRuntime(), true);
        var idle = await harness.HtmlAsync();
        Assert.Contains(idle, page);
        await WriteTransactionPreviewAsync(page.Replace(idle, downloadHtml, StringComparison.Ordinal), output, fileName);
    }

    private sealed class InvoiceDownloadHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent _root;
        private readonly InvoicePdfDownload _component;

        private InvoiceDownloadHarness(ServiceProvider services, HtmlRenderer renderer,
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent root, InvoicePdfDownload component) =>
            (_services, _renderer, _root, _component) = (services, renderer, root, component);

        public static async Task<InvoiceDownloadHarness> CreateAsync(VFMS.Client.Services.MembershipPlanApiClient api, InvoiceBrowserRuntime browser, bool canExport)
        {
            var capture = new InvoiceComponentActivator();
            var services = new ServiceCollection().AddLogging().AddSingleton(api).AddSingleton<IJSRuntime>(browser)
                .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<InvoicePdfDownload>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(InvoicePdfDownload.OrderId)] = 7, [nameof(InvoicePdfDownload.CanExport)] = canExport })));
            return new(services, renderer, root, capture.Component!);
        }

        public Task<string> HtmlAsync() => _renderer.Dispatcher.InvokeAsync(_root.ToHtmlString);

        public Task ClickAsync() => _renderer.Dispatcher.InvokeAsync(InvokeHandlerAsync);

        public async Task<Task> StartClickAsync()
        {
            Task? click = null;
            await _renderer.Dispatcher.InvokeAsync(() => { click = InvokeHandlerAsync(); });
            return click!;
        }

        private Task InvokeHandlerAsync() => ((IHandleEvent)_component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
            (Task)typeof(InvoicePdfDownload).GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_component, null)!)), null);

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class InvoiceComponentActivator : IComponentActivator
    {
        public InvoicePdfDownload? Component { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InvoicePdfDownload invoice)
                Component = invoice;
            return component;
        }
    }

    private sealed class InvoiceBrowserRuntime : IJSRuntime
    {
        public bool Fail { get; set; }
        public List<(string FileName, string ContentType, byte[] Content)> Downloads { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("vfmsInvoices.download", identifier);
            if (Fail)
                throw new JSException("Synthetic browser error");
            var reference = Assert.IsType<DotNetStreamReference>(args![2]);
            using var buffer = new MemoryStream();
            await reference.Stream.CopyToAsync(buffer, cancellationToken);
            Downloads.Add((Assert.IsType<string>(args[0]), Assert.IsType<string>(args[1]), buffer.ToArray()));
            return default!;
        }
    }
}
