using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.Operations;
using VFMS.Client.Pages.Manager;
using VFMS.Client.Components.Promotions;
using VFMS.Client.Services;
using VFMS.Contracts.Promotions;
using Xunit;

namespace VFMS.ClientTests;

public sealed class PromotionPageTests
{
    [Fact]
    public void Page_RequiresManagerRole()
    {
        var authorization = Assert.Single(typeof(Promotions).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal("Manager", authorization.Roles);
        Assert.Equal("/manager/promotions", Assert.Single(typeof(Promotions).GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>()).Template);
    }

    [Fact]
    public async Task Page_RendersAllServerStatusesReadonlyUsageAndGymTimezone()
    {
        var rows = Enum.GetValues<PromotionStatus>().Select((status, index) => PromotionApiClientTests.Response(status) with
        {
            Id = index + 1,
            Name = status == PromotionStatus.Scheduled ? "Ưu đãi hội viên tháng mười một – chiến dịch khuyến mãi với tên dài để kiểm tra màn hình nhỏ" : "Khuyến mãi " + status,
            Code = status == PromotionStatus.Scheduled ? new string('S', 50) : "SAVE20" + index
        }).ToArray();
        var html = await RenderAsync(_ => Task.FromResult(PromotionApiClientTests.Json(rows)));

        var visible = WebUtility.HtmlDecode(html);
        foreach (var label in new[] { "Sắp diễn ra", "Đang áp dụng", "Hết hạn", "Ngừng sử dụng" }) Assert.Contains(label, visible);
        Assert.True(html.IndexOf("promotion-title-2", StringComparison.Ordinal) < html.IndexOf("promotion-title-1", StringComparison.Ordinal));
        Assert.Contains("3 / 100", html);
        Assert.Contains("13/11/2026 08:30", html);
        Assert.Contains("UTC+7", html);
        Assert.Contains("aria-labelledby=\"promotion-list-title\"", html);
        Assert.DoesNotContain("type=\"number\"", html); // The list has no editable runtime usage/status fields.

        // Opt-in standalone artifact for manual responsive visual review; ordinary test runs write no files.
        if (Environment.GetEnvironmentVariable("VFMS_PROMOTION_PREVIEW") is { Length: > 0 } output)
            await WritePreviewAsync(html, output);
    }

    [Fact]
    public async Task Page_ShowsTenVouchersAndCanOpenTheSecondPage()
    {
        var rows = Enumerable.Range(1, 11).Select(id => PromotionApiClientTests.Response(PromotionStatus.Active) with
        {
            Id = id, Name = $"Ưu đãi {id:D2}", Code = $"VOUCHER-{id:D2}"
        }).ToArray();
        using var api = PromotionApiClientTests.CreateClient(_ => Task.FromResult(PromotionApiClientTests.Json(rows)));
        var capture = new PromotionActivator();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(api)
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<Promotions>();
            var firstPage = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Trang 1 / 2 · 11 voucher", firstPage);
            foreach (var row in rows.Take(10))
                Assert.Contains(row.Code, firstPage);
            Assert.DoesNotContain(rows[^1].Code, firstPage);

            await capture.Pager!.OnPage.InvokeAsync(2);

            var secondPage = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Trang 2 / 2 · 11 voucher", secondPage);
            Assert.Contains(rows[^1].Code, secondPage);
            Assert.DoesNotContain(rows[0].Code, secondPage);
        });
    }

    [Fact]
    public async Task ManagerFormShowsOnlyTheSelectedAudienceFields()
    {
        using var api = PromotionApiClientTests.CreateClient(_ => Task.FromResult(PromotionApiClientTests.Json(Array.Empty<PromotionResponse>())));
        var capture = new PromotionActivator();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(api)
            .AddSingleton<IComponentActivator>(capture).AddSingleton<NavigationManager>(new PreviewNavigation()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<Promotions>();
            var page = capture.Page!;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var start = typeof(Promotions).GetMethod("StartCreate", flags)!.CreateDelegate<Action>(page);
            await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem(start), null);
            var form = (PromotionFormModel)typeof(Promotions).GetField("_form", flags)!.GetValue(page)!;
            var create = WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.Contains("Tất cả khách hàng", create);
            Assert.Contains("Số lượt tối đa mỗi tài khoản", create);
            Assert.DoesNotContain("id=\"promotion-NewCustomerDuration\"", create);
            Assert.DoesNotContain("phần thưởng cuộc thi", create);

            foreach (var audience in new[] { PromotionAudience.NewCustomers, PromotionAudience.AssignedUsers })
            {
                await ((IHandleEvent)page).HandleEventAsync(new EventCallbackWorkItem((Action)(() => form.ChangeAudience(audience))), null);
                var rendered = view.ToHtmlString();
                var text = WebUtility.HtmlDecode(rendered);
                if (audience == PromotionAudience.NewCustomers)
                {
                    Assert.Contains("id=\"promotion-NewCustomerDuration\"", text);
                    Assert.Contains("chưa đủ 1 tháng", text);
                    Assert.DoesNotContain("phần thưởng cuộc thi", text);
                }
                else
                {
                    Assert.DoesNotContain("id=\"promotion-NewCustomerDuration\"", text);
                    Assert.Contains("phần thưởng cuộc thi", text);
                    Assert.Contains("kể cả khi người khác biết mã giảm giá", text);
                }
                if (Environment.GetEnvironmentVariable("VFMS_PROMOTION_PREVIEW") is { Length: > 0 } output)
                    await WritePreviewAsync(rendered, Path.ChangeExtension(output, audience + ".html"));
            }
        });
    }

    private sealed class PromotionActivator : IComponentActivator
    {
        public Promotions? Page { get; private set; }
        public Pager? Pager { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is Promotions page) Page = page;
            if (component is Pager pager) Pager = pager;
            return component;
        }
    }

    private sealed class PreviewNavigation : NavigationManager
    {
        public PreviewNavigation() => Initialize("https://client.example.test/", "https://client.example.test/manager/promotions");
    }

    [Fact]
    public async Task Page_ExplainsEmptyStateAndProvidesCreateAction()
    {
        var html = await RenderAsync(_ => Task.FromResult(PromotionApiClientTests.Json(Array.Empty<PromotionResponse>())));
        Assert.Contains("Chưa có khuyến mãi", WebUtility.HtmlDecode(html));
        Assert.Contains("Tạo khuyến mãi", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Page_RendersApiFailureWithRetryAndAlertSemantics()
    {
        var html = await RenderAsync(_ => Task.FromResult(PromotionApiClientTests.Json(new { code = "UNAVAILABLE" }, HttpStatusCode.ServiceUnavailable)));
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Tải lại danh sách", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Chưa có khuyến mãi", WebUtility.HtmlDecode(html));
    }

    private static async Task<string> RenderAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        using var api = PromotionApiClientTests.CreateClient(handler);
        var services = new ServiceCollection().AddLogging().AddSingleton(api);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<Promotions>();
            return component.ToHtmlString();
        });
    }

    private static async Task WritePreviewAsync(string html, string output)
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var wwwroot = Path.Combine(workspace, "src/VFMS.Client/wwwroot");
        var index = await File.ReadAllTextAsync(Path.Combine(wwwroot, "index.html"));
        var head = index[(index.IndexOf("<head>", StringComparison.Ordinal) + 6)..index.IndexOf("</head>", StringComparison.Ordinal)];
        head = head.Replace("<base href=\"/\" />", "");
        var tailwindStart = index.IndexOf("<script src=\"https://cdn.tailwindcss.com\"", StringComparison.Ordinal);
        var tailwindEnd = index.IndexOf("</script>", index.IndexOf("</script>", tailwindStart, StringComparison.Ordinal) + 9, StringComparison.Ordinal) + 9;
        var tailwind = index[tailwindStart..tailwindEnd];
        var css = new StringBuilder();
        foreach (var path in new[] { "css/app.css", "css/workspace.css" })
            css.Append(await File.ReadAllTextAsync(Path.Combine(wwwroot, path)));
        await File.WriteAllTextAsync(output, $"<!doctype html><html lang=\"vi\"><head>{head}<style>{css}</style></head><body><div class=\"workspace-shell workspace-shell--manager\"><aside class=\"workspace-sidebar\"><p class=\"workspace-heading\">VICTOR FITNESS</p><p class=\"workspace-role\">Manager</p><nav class=\"workspace-nav\"><p class=\"workspace-sidebar-label\">MARKETING</p><a class=\"workspace-nav-link workspace-nav-link--active\" href=\"#\">Khuyến mãi</a></nav></aside><div class=\"workspace-content\"><header class=\"workspace-topbar\"><span class=\"workspace-heading\">VFMS / Manager</span></header><main class=\"workspace-main\">{html}</main></div></div>{tailwind}</body></html>");
    }
}
