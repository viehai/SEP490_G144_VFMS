namespace VFMS.Contracts.Common;

/// <summary>
/// Images uploaded for equipment and supplement products. An uploaded image is served at <c>/api/images/{key}</c>,
/// and that path is what a record stores in its image field (an http(s) link to an outside image also works).
/// </summary>
public static class UploadedImages
{
    public const string PathPrefix = "/api/images/";
    public const int MaximumBytes = 5 * 1024 * 1024;
    public static readonly string[] ContentTypes = ["image/png", "image/jpeg", "image/webp"];

    public static string PathFor(Guid key) => PathPrefix + key.ToString("N");

    public static bool IsUploadedPath(string? url) =>
        url is not null && url.StartsWith(PathPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(url[PathPrefix.Length..], "N", out _);
}

/// <summary><see cref="Url"/> is the path to save in the record's image field.</summary>
public sealed record UploadedImageResponse(string Url, string FileName, string ContentType, int SizeBytes);
