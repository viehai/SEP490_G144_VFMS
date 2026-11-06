namespace VFMS.Application.Settings;

public sealed class PasswordResetSettings
{
    public string ResetUrl { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int RequestCooldownSeconds { get; set; } = 60;

    public bool IsValid => TokenLifetimeMinutes is >= 1 and <= 60 &&
        RequestCooldownSeconds is >= 30 and <= 3600 &&
        Uri.TryCreate(ResetUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
