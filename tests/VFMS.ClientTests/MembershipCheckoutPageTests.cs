using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.Memberships;
using VFMS.Client.Pages;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Fact]
    public void CheckoutPage_AllowsCustomerAndMember()
    {
        var authorization = Assert.Single(typeof(MembershipCheckout).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Customer,Member", authorization.Roles);
    }

    [Theory]
    [InlineData(50_000, "450.000 ₫")]
    [InlineData(100_000, "400.000 ₫")]
    public async Task CheckoutVoucherPanel_DisplaysFixedOrPercentageServerAmountsWithoutRecalculation(int discount, string payable)
    {
        var html = WebUtility.HtmlDecode(await RenderVoucherPanelAsync(VoucherPurchase(discount), true));
        Assert.Contains("Tạm tính", html);
        Assert.Contains("500.000 ₫", html);
        Assert.Contains(payable, html);
        Assert.Contains("SAVE20", html);
        Assert.Contains("November promotion", html);
        Assert.Contains("Đã giữ mã", html);
        Assert.Contains("Bỏ mã", html);
        Assert.DoesNotContain("id=\"checkout-voucher-code\"", html);
    }

    [Fact]
    public async Task CheckoutVoucherPanel_NoVoucherShowsOptionalInputAndUnchangedFullPrice()
    {
        var html = WebUtility.HtmlDecode(await RenderVoucherPanelAsync(VoucherPurchase(0), true));
        Assert.Contains("id=\"checkout-voucher-code\"", html);
        Assert.Contains("maxlength=\"50\"", html);
        Assert.Contains("tùy chọn", html);
        Assert.Contains("Mỗi đơn hàng chỉ áp dụng một mã", html);
        Assert.Contains("500.000 ₫", html);
        Assert.Contains("0 ₫", html);
        Assert.DoesNotContain("Bỏ mã", html);
    }

    [Fact]
    public async Task CheckoutVoucherPanel_InFlightApplyDisablesInputAndDuplicateSubmission()
    {
        var html = WebUtility.HtmlDecode(await RenderVoucherPanelAsync(VoucherPurchase(0), true, busy: true, applying: true));
        Assert.Contains("aria-busy=\"true\"", html);
        Assert.Matches(new Regex("<fieldset[^>]*disabled", RegexOptions.CultureInvariant), html);
        Assert.Matches(new Regex("<button[^>]*disabled[^>]*>Đang áp dụng", RegexOptions.CultureInvariant), html);
    }

    [Fact]
    public async Task CheckoutVoucherPanel_ShowsSpecificErrorsAndSuccessAnnouncements()
    {
        var html = WebUtility.HtmlDecode(await RenderVoucherPanelAsync(VoucherPurchase(0), true,
            error: "Mỗi đơn hàng chỉ được áp dụng một mã giảm giá.", notice: "Đã cập nhật tổng thanh toán."));
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Mỗi đơn hàng chỉ được áp dụng một mã giảm giá.", html);
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("Đã cập nhật tổng thanh toán.", html);

        if (Environment.GetEnvironmentVariable("VFMS_UC19_PREVIEW_DIR") is { Length: > 0 } output)
        {
            var purchase = VoucherPurchase(0);
            var checkout = await RenderCheckoutPageAsync(purchase);
            var initialPanel = await RenderVoucherPanelAsync(purchase, true);
            Assert.Contains(initialPanel, checkout);
            var validationError = await RenderVoucherPanelAsync(purchase, true,
                error: "Đơn hàng chưa đạt giá trị tối thiểu để áp dụng mã giảm giá này. Vui lòng kiểm tra điều kiện của chương trình.");
            var removedVoucher = await RenderVoucherPanelAsync(purchase, true,
                notice: "Đã bỏ mã giảm giá. Tổng thanh toán đã được cập nhật.");
            await WriteCheckoutPreviewAsync(checkout.Replace(initialPanel, validationError, StringComparison.Ordinal), output,
                "checkout-validation-error.html");
            await WriteCheckoutPreviewAsync(checkout.Replace(initialPanel, removedVoucher, StringComparison.Ordinal), output,
                "checkout-removed-voucher.html");
        }
    }

    [Fact]
    public async Task CheckoutVoucherPanel_ConfirmedVoucherHasNoMutationControls()
    {
        var purchase = VoucherPurchase() with { OrderStatus = "Paid", AppliedVoucher = new("SAVE20", "November promotion", 100_000m, "Confirmed") };
        var html = WebUtility.HtmlDecode(await RenderVoucherPanelAsync(purchase, false));
        Assert.Contains("Đã sử dụng cho đơn hàng này.", html);
        Assert.DoesNotContain("Bỏ mã", html);
        Assert.DoesNotContain("id=\"checkout-voucher-code\"", html);
    }

    [Fact]
    public async Task CheckoutVoucherPanel_ConcurrentRequestDisablesRemoval()
    {
        var html = await RenderVoucherPanelAsync(VoucherPurchase(), true, busy: true);
        Assert.Matches(new Regex("<button[^>]*disabled", RegexOptions.CultureInvariant), html);
    }

    [Theory]
    [InlineData(0, "checkout-no-voucher.html")]
    [InlineData(100_000, "checkout-applied-voucher.html")]
    [InlineData(500_000, "checkout-free-order.html")]
    public async Task CheckoutPage_RendersActualPendingOrderWithVoucherAndPaymentState(int discount, string previewName)
    {
        var purchase = VoucherPurchase(discount) with
        {
            OrderNumber = "ORD-20261113-ABCDE1234567890",
            AppliedVoucher = discount == 0 ? null : new("SAVE20NOVEMBERWITHAVERYLONGVOUCHERCODE", "Khuyến mãi hội viên tháng mười một với tên chiến dịch dài trên thiết bị di động", discount, "Reserved")
        };
        var rendered = await RenderCheckoutPageAsync(purchase);
        var html = WebUtility.HtmlDecode(rendered);
        Assert.Contains("Tổng thanh toán", html);
        Assert.Contains("Tạm tính", html);
        Assert.Contains("Giảm giá", html);
        Assert.Contains("Chờ thanh toán", html);
        if (discount == 500_000)
        {
            Assert.Contains("Hoàn tất đơn hàng miễn phí", html);
            Assert.DoesNotContain("Mã VietQR thanh toán đơn hàng", html);
            Assert.DoesNotContain("Thanh toán thành công", html);
        }
        else
            Assert.Contains("Mã VietQR thanh toán đơn hàng", html);

        if (Environment.GetEnvironmentVariable("VFMS_UC19_PREVIEW_DIR") is { Length: > 0 } output)
            await WriteCheckoutPreviewAsync(rendered, output, previewName);
    }

    private static async Task<string> RenderVoucherPanelAsync(MembershipPurchaseResponse purchase, bool canManage,
        bool busy = false, bool applying = false, string? error = null, string? notice = null)
    {
        var services = new ServiceCollection().AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CheckoutVoucherPanel.Purchase)] = purchase,
                [nameof(CheckoutVoucherPanel.CanManageVoucher)] = canManage,
                [nameof(CheckoutVoucherPanel.IsBusy)] = busy,
                [nameof(CheckoutVoucherPanel.IsApplying)] = applying,
                [nameof(CheckoutVoucherPanel.Error)] = error,
                [nameof(CheckoutVoucherPanel.Notice)] = notice
            });
            return (await renderer.RenderComponentAsync<CheckoutVoucherPanel>(parameters)).ToHtmlString();
        });
    }

    private static async Task<string> RenderCheckoutPageAsync(MembershipPurchaseResponse purchase)
    {
        using var api = CreateClient(_ => Task.FromResult(Json(purchase)), role: "Customer");
        var services = new ServiceCollection().AddLogging().AddSingleton(api);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MembershipCheckout>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(MembershipCheckout.OrderNumber)] = purchase.OrderNumber
            }))).ToHtmlString());
    }

    private static async Task WriteCheckoutPreviewAsync(string html, string output, string fileName)
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
        var css = new StringBuilder(await File.ReadAllTextAsync(Path.Combine(wwwroot, "css/app.css")));
        await File.WriteAllTextAsync(Path.Combine(output, fileName), $"<!doctype html><html lang=\"vi\"><head>{head}<style>{css}</style></head><body class=\"bg-vfms-bg text-vfms-text\">{html}{tailwind}</body></html>");
    }
}
