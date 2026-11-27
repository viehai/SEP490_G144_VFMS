using VFMS.Contracts.Common;

namespace VFMS.Client.Services;

internal static class AssetUrls
{
    public static string? Resolve(Uri apiBase, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("/api/trainers/", StringComparison.Ordinal) || UploadedImages.IsUploadedPath(path))
            return new Uri(apiBase, path.TrimStart('/')).AbsoluteUri;
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri : null;
    }
}
