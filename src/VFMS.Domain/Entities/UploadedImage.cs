using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// An image uploaded by staff for equipment or a supplement product, kept in the database like the other uploaded files.
/// It is served at <c>/api/images/{ImageKey}</c>; records store that path in their image field.
/// </summary>
public class UploadedImage : BaseEntity
{
    /// <summary>Public key used in the image's address, so ids cannot be guessed in order.</summary>
    public Guid ImageKey { get; set; }
    public string FileName { get; set; } = string.Empty;
    /// <summary>image/png, image/jpeg or image/webp, checked against the file's own bytes.</summary>
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string UploadedByUserId { get; set; } = string.Empty;
}
