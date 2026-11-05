namespace VFMS.Client.Auth;

public static class AuthNavigation
{
    public static string SafeReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "/";
        var decoded = Uri.UnescapeDataString(value);
        if (!decoded.StartsWith('/') || decoded.StartsWith("//") || decoded.Contains('\\') ||
            decoded.Any(char.IsControl) || decoded.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)) return "/";
        return value;
    }
}
