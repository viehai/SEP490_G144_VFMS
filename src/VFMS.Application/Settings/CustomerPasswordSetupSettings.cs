namespace VFMS.Application.Settings;

public sealed class CustomerPasswordSetupSettings
{
    public const int DefaultTokenLifetimeHours = 48;
    private static readonly string[] NonPublicDnsSuffixes = [".local", ".internal", ".test", ".invalid", ".example", ".onion"];

    public string SetupUrl { get; set; } = string.Empty;
    public int TokenLifetimeHours { get; set; } = DefaultTokenLifetimeHours;
    public bool AllowLoopback { get; init; }

    public bool IsValid =>
        TokenLifetimeHours == DefaultTokenLifetimeHours &&
        Uri.TryCreate(SetupUrl, UriKind.Absolute, out var uri) &&
        HasAllowedOrigin(uri) &&
        string.Equals(uri.AbsolutePath.TrimEnd('/'), "/auth/setup-password", StringComparison.Ordinal) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    public static CustomerPasswordSetupSettings FromConfiguration(
        string? setupUrl,
        bool isDevelopment,
        int? tokenLifetimeHours = null) =>
        new()
        {
            SetupUrl = setupUrl?.Trim().TrimEnd('/') ?? string.Empty,
            AllowLoopback = isDevelopment,
            TokenLifetimeHours = tokenLifetimeHours ?? DefaultTokenLifetimeHours
        };

    private bool HasAllowedOrigin(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        var isLoopback = uri.IsLoopback ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        if (isLoopback)
            return AllowLoopback && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        return uri.Scheme == Uri.UriSchemeHttps &&
            uri.HostNameType == UriHostNameType.Dns &&
            host.Contains('.') &&
            !NonPublicDnsSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
