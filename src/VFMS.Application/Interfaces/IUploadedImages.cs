using VFMS.Contracts.Common;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IUploadedImageRepository
{
    void Add(UploadedImage image);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<UploadedImage?> GetAsync(Guid key, CancellationToken cancellationToken = default);
}

/// <summary>Images for equipment and supplement products (upload by staff, public download).</summary>
public interface IUploadedImageService
{
    Task<Result<UploadedImageResponse>> UploadAsync(
        string userId, string? fileName, long length, Stream content, CancellationToken cancellationToken = default);
    Task<Result<UploadedImage>> GetAsync(Guid key, CancellationToken cancellationToken = default);
}
