namespace VFMS.Application.Settings;

/// <summary>Where the trainer password setup page lives (GBR-04). Falls back to the password reset site's /auth/trainer-setup.</summary>
public sealed class TrainerSetupLinkSettings
{
    /// <summary>TrainerMail:SetupPageUrl.</summary>
    public string? SetupPageUrl { get; init; }
    /// <summary>PasswordReset:ResetUrl, used for its origin when no setup page is configured.</summary>
    public string? ResetUrl { get; init; }

    public string Build(string token)
    {
        var useResetUrl = string.IsNullOrWhiteSpace(SetupPageUrl);
        var url = useResetUrl ? ResetUrl : SetupPageUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Configure the trainer password setup page URL.");

        var link = new UriBuilder(uri) { Fragment = string.Empty };
        if (useResetUrl)
        {
            link.Path = "/auth/trainer-setup";
            link.Query = string.Empty;
        }
        var existingQuery = link.Query.TrimStart('?');
        var tokenQuery = "token=" + Uri.EscapeDataString(token);
        link.Query = existingQuery.Length == 0 ? tokenQuery : existingQuery + "&" + tokenQuery;
        return link.Uri.AbsoluteUri;
    }
}
