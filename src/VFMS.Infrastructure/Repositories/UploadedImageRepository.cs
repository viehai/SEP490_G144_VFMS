using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class UploadedImageRepository(VfmsDbContext context) : IUploadedImageRepository
{
    public void Add(UploadedImage image) => context.UploadedImages.Add(image);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);

    public Task<UploadedImage?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        context.UploadedImages.AsNoTracking().FirstOrDefaultAsync(image => image.ImageKey == key, cancellationToken);
}
