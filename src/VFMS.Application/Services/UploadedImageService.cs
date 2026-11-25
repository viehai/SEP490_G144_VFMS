using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;

namespace VFMS.Application.Services;

/// <summary>
/// Stores equipment and supplement photos in the database (like the other uploaded files) and serves them back.
/// The type comes from the file's own bytes, so a renamed file or a wrong Content-Type cannot slip through.
/// </summary>
public sealed class UploadedImageService(IUploadedImageRepository repository) : IUploadedImageService
{
    public async Task<Result<UploadedImageResponse>> UploadAsync(
        string userId, string? fileName, long length, Stream content, CancellationToken cancellationToken = default)
    {
        if (length <= 0)
            return Result<UploadedImageResponse>.Failure("The image file is empty.", "IMAGE_EMPTY");
        if (length > UploadedImages.MaximumBytes)
            return Result<UploadedImageResponse>.Failure("The image must be 5 MB or smaller.", "IMAGE_TOO_LARGE", 413);

        // Read at most one byte past the limit, so a stream longer than it claimed is still caught.
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > UploadedImages.MaximumBytes)
                return Result<UploadedImageResponse>.Failure("The image must be 5 MB or smaller.", "IMAGE_TOO_LARGE", 413);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
            return Result<UploadedImageResponse>.Failure("The image file is empty.", "IMAGE_EMPTY");
        if (DetectType(bytes) is not { } contentType)
            return Result<UploadedImageResponse>.Failure(
                "Only PNG, JPEG or WebP images can be uploaded.", "IMAGE_INVALID_TYPE", 415);

        var image = new UploadedImage
        {
            ImageKey = Guid.NewGuid(),
            FileName = CleanFileName(fileName, contentType),
            ContentType = contentType,
            Content = bytes,
            UploadedByUserId = userId,
        };
        repository.Add(image);
        await repository.SaveChangesAsync(cancellationToken);
        return Result<UploadedImageResponse>.Success(new UploadedImageResponse(
            UploadedImages.PathFor(image.ImageKey), image.FileName, image.ContentType, bytes.Length), 201);
    }

    public async Task<Result<UploadedImage>> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        await repository.GetAsync(key, cancellationToken) is { } image
            ? Result<UploadedImage>.Success(image)
            : Result<UploadedImage>.Failure("Image was not found.", "IMAGE_NOT_FOUND", 404);

    /// <summary>PNG, JPEG or WebP from the file signature; null for anything else.</summary>
    public static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    /// <summary>The name shown when the image is downloaded: no folders, safe characters, the right extension.</summary>
    public static string CleanFileName(string? fileName, string contentType)
    {
        var extension = contentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? ""));
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        if (safe.Length == 0) safe = "anh";
        return (safe.Length > 100 ? safe[..100] : safe) + extension;
    }
}
