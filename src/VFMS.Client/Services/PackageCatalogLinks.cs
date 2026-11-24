namespace VFMS.Client.Services;

internal static class PackageCatalogLinks
{
    public static string Catalog(string type, int? page = null) =>
        WithPage($"/membership-plans?type={type.ToLowerInvariant()}", page, '&');

    public static string Detail(string type, int id, int? page = null) =>
        WithPage($"/membership-plans/{type.ToLowerInvariant()}/{id}", page, '?');

    public static string Trainers(int packageId) => $"/trainers?package={packageId}";

    private static string WithPage(string path, int? page, char separator) =>
        page is > 1 ? $"{path}{separator}page={page}" : path;
}
