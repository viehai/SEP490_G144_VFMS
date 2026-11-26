using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Layout;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class RouterAuthenticationTests
{
    public static TheoryData<string, Type, string[]> DashboardMenus => new()
    {
        {
            "Manager", typeof(ManagerAdminLayout),
            [
                "Tổng quan & báo cáo", "/manager/dashboard", "/manager/business-reports",
                "Vận hành phòng tập", "/staff/check-ins", "/kiosk", "/manager/yoga", "/manager/competitions",
                "Gói hội viên & chính sách", "/manager/membership-plans", "/manager/membership-freeze-policy",
                "/manager/membership-freeze-requests", "/manager/general-policies",
                "Huấn luyện viên & PT", "/manager/trainer-applications", "/manager/pt-packages",
                "/manager/pt-freeze-requests", "/manager/pt-attendance-disputes",
                "Thiết bị & mua sắm", "/manager/equipment", "/manager/exercises", "/manager/equipment-issues", "/manager/purchase-proposals",
                "Tủ đồ", "/manager/lockers", "/manager/locker-clearances",
                "Bán hàng & Marketing", "/manager/supplement-products", "/manager/supplement-orders", "/manager/promotions",
                "Khách hàng", "/manager/customers/create", "/manager/feedback", "/manager/consultations", "/manager/promotions"
            ]
        },
        {
            "Admin", typeof(ManagerAdminLayout),
            [
                "Tổng quan", "/admin/dashboard", "/admin/dashboard#quick-actions",
                "Hoạt động & báo cáo", "/admin/business-reports",
                "Quản trị hệ thống", "/admin/banned-words", "/admin/audit-logs"
            ]
        },
        {
            "Staff", typeof(StaffPTLayout),
            [
                "Tổng quan", "/staff/dashboard", "/staff/dashboard#quick-actions",
                "Lễ tân & điểm danh", "/staff/check-ins", "/kiosk", "/staff/yoga",
                "Khách hàng", "/staff/customers/create", "/staff/consultations", "/staff/feedback",
                "Đơn hàng", "/staff/membership-cash-receipt", "/staff/supplement-orders",
                "Tủ đồ", "/staff/lockers", "/staff/locker-contracts",
                "Thiết bị & mua sắm", "/staff/equipment-issues", "/staff/purchase-proposals",
                "Hoạt động", "/staff/competitions"
            ]
        },
        {
            "PT", typeof(StaffPTLayout),
            [
                "Lịch huấn luyện", "/pt/availability", "/pt/teaching-schedule",
                "Hội viên", "/pt/assigned-members",
                "Hồ sơ", "/pt/trainer-profile"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(DashboardMenus))]
    public async Task DashboardMenuShowsSelectedLinksInTheirApprovedGroups(string role, Type layout, string[] expected)
    {
        var nav = await RenderWorkspaceMenu(layout, $"/{role.ToLowerInvariant()}/dashboard", role);
        var entries = nav.Descendants().Where(e => e.Name == "h2" || e.Name == "a")
            .Select(e => e.Name == "a" ? (string?)e.Attribute("href") : e.Value.Trim());
        Assert.Equal(expected, entries);
    }

    [Theory]
    [InlineData(typeof(ManagerAdminLayout), "Manager", "/manager/promotions?status=Active", "/manager/promotions", "page")]
    [InlineData(typeof(ManagerAdminLayout), "Admin", "/admin/dashboard", "/admin/dashboard", "page")]
    [InlineData(typeof(StaffPTLayout), "Staff", "/staff/dashboard#quick-actions", "/staff/dashboard#quick-actions", "location")]
    public async Task SidebarMarksOnlyTheCurrentDestination(Type layout, string role, string path, string href, string current)
    {
        var nav = await RenderWorkspaceMenu(layout, path, role);
        var active = Assert.Single(nav.Descendants("a").Where(link => link.Attribute("aria-current") is not null));
        Assert.Equal(href, active.Attribute("href")?.Value);
        Assert.Equal(current, active.Attribute("aria-current")?.Value);
    }

    [Theory]
    [InlineData(typeof(StaffPTLayout), "/staff/check-ins", "Manager", "/manager/dashboard", typeof(ManagerAdminLayout))]
    [InlineData(typeof(ManagerAdminLayout), "/manager/competitions", "Staff", "/staff/dashboard", typeof(StaffPTLayout))]
    public async Task SharedPagesKeepTheUsersDashboardMenu(Type sharedLayout, string path, string role, string dashboard, Type ownLayout)
    {
        var shared = await RenderWorkspaceMenu(sharedLayout, path, role);
        var own = await RenderWorkspaceMenu(ownLayout, dashboard, role);
        Assert.Equal(MenuLinks(own), MenuLinks(shared));
    }

    [Theory]
    [InlineData(typeof(StaffPTLayout), "/pt/yoga", "PT,YogaInstructor", "/pt/availability /pt/teaching-schedule /pt/yoga /pt/assigned-members /pt/trainer-profile")]
    [InlineData(typeof(StaffPTLayout), "/pt/yoga", "YogaInstructor", "/profile /competitions / /pt/trainer-profile /pt/yoga")]
    [InlineData(typeof(AccountLayout), "/my-pt-packages", "Member", "/profile /competitions / /my-vouchers /my-memberships /my-pt-packages")]
    [InlineData(typeof(AccountLayout), "/my-pt-packages", "Customer", "/profile /competitions / /my-vouchers /my-memberships /my-pt-packages")]
    public async Task SharedLayoutsOnlyShowTheLinksForTheAuthenticatedRoles(Type layout, string path, string roles, string expected)
    {
        var nav = await RenderWorkspaceMenu(layout, path, roles.Split(','));
        Assert.Equal(expected.Split(' ').Order(StringComparer.Ordinal), MenuLinks(nav).Order(StringComparer.Ordinal));
    }

    private static string[] MenuLinks(XElement menu) =>
        menu.Descendants("a").Select(a => a.Attribute("href")!.Value).ToArray();

    private static async Task<XElement> RenderWorkspaceMenu(Type layout, string path, params string[] roles)
    {
        var user = new LoginResponse("test-access", "test-refresh", "Bearer", 1800,
            "menu-user", "Test User", "menu@example.test", roles);
        var storage = new Storage(JsonSerializer.Serialize(new StoredAuthSession(user, DateTimeOffset.UtcNow.AddHours(1), true)));
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Menu rendering made an API call.")));
        var session = new AuthSession(storage, new AuthApiClient(http));
        using var services = new ServiceCollection().AddLogging().AddAuthorizationCore()
            .AddSingleton(session).AddSingleton<AuthenticationStateProvider>(session).AddCascadingAuthenticationState()
            .AddSingleton<IJSRuntime>(storage).AddSingleton<NavigationManager>(new Navigation(path)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync(layout)).ToHtmlString());
        var nav = Assert.Single(Regex.Matches(html, @"<nav\b[^>]*>.*?</nav>", RegexOptions.Singleline));
        return XElement.Parse(nav.Value);
    }
}
