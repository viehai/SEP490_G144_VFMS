using System.Globalization;

namespace VFMS.Contracts.Common;

public static class AvatarUrls
{
    public static bool IsInternal(string? value)
    {
        if (UploadedImages.IsUploadedPath(value)) return true;
        if (value == null) return false;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 && parts[0] == "api" && parts[1] == "trainers" && parts[3] == "avatar" &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 &&
            value == $"/api/trainers/{id.ToString(CultureInfo.InvariantCulture)}/avatar";
    }
}
