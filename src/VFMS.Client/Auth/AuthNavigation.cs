using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using VFMS.Client.Pages.Admin;
using VFMS.Client.Pages.Manager;
using VFMS.Client.Pages.PT;
using VFMS.Client.Pages.Staff;

namespace VFMS.Client.Auth;

public static class AuthNavigation
{
    // Navigation preference for accounts with multiple roles, not an authorization hierarchy.
    private static readonly Type[] Dashboards =
        [typeof(AdminDashboard), typeof(ManagerDashboard), typeof(StaffDashboard), typeof(PTDashboard)];
    private static readonly (string Template, Type Page)[] Pages = typeof(App).Assembly.GetExportedTypes()
        .SelectMany(page => page.GetCustomAttributes<RouteAttribute>()
            .Select(route => (route.Template, page))).ToArray();
    private static readonly Uri LocalOrigin = new("https://vfms.invalid/");

    public static string DefaultRoute(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return "/";
        var dashboard = Dashboards.FirstOrDefault(page => page.GetCustomAttributes<AuthorizeAttribute>()
            .Any(attribute => attribute.Roles?.Split(',').Any(role => user.IsInRole(role.Trim())) == true));
        return dashboard?.GetCustomAttribute<RouteAttribute>()?.Template ?? "/";
    }

    public static async Task<string> LoginDestinationAsync(ClaimsPrincipal user, string? returnUrl,
        IAuthorizationPolicyProvider policies, IAuthorizationService authorization)
    {
        var fallback = DefaultRoute(user);
        var candidate = LocalReturnUrl(returnUrl);
        if (user.Identity?.IsAuthenticated != true || candidate is null) return fallback;

        var path = Uri.UnescapeDataString(new Uri(LocalOrigin, candidate).AbsolutePath);
        var page = Pages.FirstOrDefault(route => Matches(route.Template, path)).Page;
        if (page is null) return fallback;
        if (page.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)) return candidate;

        // Use the same page authorization metadata as AuthorizeRouteView, including shared-role pages.
        var policy = await AuthorizationPolicy.CombineAsync(policies, page.GetCustomAttributes<AuthorizeAttribute>());
        return policy is null || (await authorization.AuthorizeAsync(user, resource: null, policy)).Succeeded
            ? candidate : fallback;
    }

    public static string SafeReturnUrl(string? value) => LocalReturnUrl(value) ?? "/";

    private static string? LocalReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = Uri.UnescapeDataString(value);
        if (!decoded.StartsWith('/') || decoded.StartsWith("//") || decoded.Contains('\\') ||
            decoded.Any(char.IsControl) || !Uri.TryCreate(LocalOrigin, value, out var local) ||
            local.Scheme != LocalOrigin.Scheme || local.Authority != LocalOrigin.Authority ||
            Uri.UnescapeDataString(local.AbsolutePath).StartsWith("/auth", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    private static bool Matches(string template, string path)
    {
        var expected = template.Trim('/').Split('/');
        var actual = path.Trim('/').Split('/');
        if (expected.Length != actual.Length) return false;
        for (var index = 0; index < expected.Length; index++)
        {
            var segment = expected[index];
            if (segment.StartsWith('{') && segment.EndsWith('}'))
            {
                if (string.IsNullOrEmpty(actual[index])) return false;
                var parameter = segment[1..^1].Split(':');
                if (parameter.Length > 1 && (parameter[1] != "int" ||
                    !int.TryParse(actual[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) return false;
            }
            else if (!segment.Equals(actual[index], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
