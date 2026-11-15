using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Client.Auth;
using VFMS.Client.Pages.Admin;
using VFMS.Client.Pages.Manager;
using VFMS.Client.Pages.PT;
using VFMS.Client.Pages.Staff;
using Xunit;

namespace VFMS.ClientTests;

public sealed class AuthNavigationTests
{
    [Theory]
    [InlineData("Admin", "/admin/dashboard")]
    [InlineData("Manager", "/manager/dashboard")]
    [InlineData("Staff", "/staff/dashboard")]
    [InlineData("PT", "/pt/dashboard")]
    [InlineData("Customer", "/")]
    [InlineData("Member", "/")]
    [InlineData("Unknown", "/")]
    [InlineData("PT,Staff,Manager,Admin", "/admin/dashboard")]
    [InlineData("Admin,Manager,Staff,PT", "/admin/dashboard")]
    [InlineData("Customer,Staff,Manager", "/manager/dashboard")]
    [InlineData("PT,Staff", "/staff/dashboard")]
    public void DefaultDestination_UsesCurrentRolesAndDeterministicWorkspacePriority(string roles, string expected) =>
        Assert.Equal(expected, AuthNavigation.DefaultRoute(Principal(roles.Split(','))));

    [Fact]
    public void DefaultDestination_AnonymousIdentityCannotUseAnUntrustedRoleClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));
        Assert.Equal("/", AuthNavigation.DefaultRoute(user));
    }

    [Theory]
    [InlineData("Admin", null, "/admin/dashboard")]
    [InlineData("Manager", "", "/manager/dashboard")]
    [InlineData("Staff", "/staff/equipment-issues?status=Open", "/staff/equipment-issues?status=Open")]
    [InlineData("Manager", "/manager/promotions", "/manager/promotions")]
    [InlineData("Admin", "/manager/business-reports?period=month", "/manager/business-reports?period=month")]
    [InlineData("Manager", "/admin/business-reports", "/admin/business-reports")]
    [InlineData("Staff", "/manager/general-policies", "/manager/general-policies")]
    [InlineData("Customer", "/membership-purchases/ORD-TEST?tab=payment", "/membership-purchases/ORD-TEST?tab=payment")]
    [InlineData("Member", "/membership-purchases/ORD-TEST?tab=payment#voucher", "/membership-purchases/ORD-TEST?tab=payment#voucher")]
    [InlineData("Member", "/my-memberships/17", "/my-memberships/17")]
    [InlineData("PT", "/pt/dashboard", "/pt/dashboard")]
    [InlineData("Customer", null, "/")]
    [InlineData("Member", null, "/")]
    [InlineData("Admin", "/", "/")]
    public async Task ReturnDestination_PreservesExistingAuthorizedPagesAndQueryStrings(string role, string? candidate, string expected)
    {
        using var services = AuthorizationServices();
        Assert.Equal(expected, await ResolveAsync(services, Principal(role), candidate));
    }

    [Theory]
    [InlineData("Staff", "/admin/dashboard", "/staff/dashboard")]
    [InlineData("Manager", "/staff/dashboard", "/manager/dashboard")]
    [InlineData("Admin", "/manager/dashboard", "/admin/dashboard")]
    [InlineData("PT", "/staff/membership-cash-receipt", "/pt/dashboard")]
    [InlineData("Customer", "/admin/audit-logs", "/")]
    [InlineData("Member", "/manager/promotions", "/")]
    [InlineData("Staff", "/does-not-exist", "/staff/dashboard")]
    [InlineData("Member", "/my-memberships/not-an-integer", "/")]
    public async Task ReturnDestination_RejectsUnauthorizedOrUnmatchedRoutes(string role, string candidate, string expected)
    {
        using var services = AuthorizationServices();
        Assert.Equal(expected, await ResolveAsync(services, Principal(role), candidate));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    [InlineData("/%2fexample.com")]
    [InlineData("/%5cexample.com")]
    [InlineData("/%0aadmin/dashboard")]
    [InlineData("/auth/login")]
    [InlineData("/auth/login?returnUrl=/admin/dashboard")]
    [InlineData("/%61uth/login")]
    [InlineData("/./auth/login")]
    public async Task ReturnDestination_UnsafeOrAuthenticationLoopReturnsToRoleDashboard(string candidate)
    {
        using var services = AuthorizationServices();
        Assert.Equal("/staff/dashboard", await ResolveAsync(services, Principal("Staff"), candidate));
    }

    [Fact]
    public async Task ReturnDestination_AnonymousUserCannotEnterAnAuthorizedPage()
    {
        using var services = AuthorizationServices();
        Assert.Equal("/", await ResolveAsync(services, new ClaimsPrincipal(new ClaimsIdentity()), "/admin/dashboard"));
    }

    [Theory]
    [InlineData(typeof(AdminDashboard), "Admin")]
    [InlineData(typeof(ManagerDashboard), "Manager")]
    [InlineData(typeof(StaffDashboard), "Staff")]
    [InlineData(typeof(PTDashboard), "PT")]
    public async Task DirectDashboardRoutes_AuthorizationMetadataDeniesOtherRolesAndAnonymous(Type page, string allowedRole)
    {
        using var services = AuthorizationServices();
        var metadata = page.GetCustomAttributes<AuthorizeAttribute>().ToArray();
        Assert.NotEmpty(metadata);
        var policy = await AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(), metadata);
        Assert.NotNull(policy);
        var authorization = services.GetRequiredService<IAuthorizationService>();

        foreach (var role in new[] { "Admin", "Manager", "Staff", "PT", "Customer", "Member" })
        {
            var result = await authorization.AuthorizeAsync(Principal(role), resource: null, policy!);
            Assert.Equal(role == allowedRole, result.Succeeded);
        }
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), resource: null, policy!)).Succeeded);
    }

    private static ClaimsPrincipal Principal(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(role => new Claim(ClaimTypes.Role, role)), "TestAuthentication"));

    private static ServiceProvider AuthorizationServices() =>
        new ServiceCollection().AddLogging().AddAuthorizationCore().BuildServiceProvider();

    private static Task<string> ResolveAsync(IServiceProvider services, ClaimsPrincipal user, string? candidate) =>
        AuthNavigation.LoginDestinationAsync(user, candidate,
            services.GetRequiredService<IAuthorizationPolicyProvider>(), services.GetRequiredService<IAuthorizationService>());
}
