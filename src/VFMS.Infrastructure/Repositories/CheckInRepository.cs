using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class CheckInRepository : AuditedRepositoryBase, ICheckInRepository
{
    public CheckInRepository(VfmsDbContext context) : base(context) { }

    // IgnoreQueryFilters: an erased (soft-deleted) record is reused when the user registers again (1:1 with user).
    public Task<FaceData?> GetFaceDataAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.FaceData.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<CheckIn>> GetActiveCheckInsAsync(CancellationToken cancellationToken = default) =>
        await Context.CheckIns.AsNoTracking()
            .Include(c => c.Member)
            .Include(c => c.HandledByUser)
            .Where(c => c.Status == CheckInStatus.Active && c.CheckoutTime == null)
            .ToListAsync(cancellationToken);

    public Task<int> CountCheckInsSinceAsync(DateTime utcFrom, CancellationToken cancellationToken = default) =>
        Context.CheckIns.CountAsync(c => c.CheckinTime >= utcFrom, cancellationToken);
}
