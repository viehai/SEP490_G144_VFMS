using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

/// <summary>Class opening requests (GBR-128..GBR-130).</summary>
public sealed class YogaClassOpeningRepository : AuditedRepositoryBase, IYogaClassOpeningRepository
{
    public YogaClassOpeningRepository(VfmsDbContext context) : base(context) { }

    public Task<YogaClassOpeningRequest?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Context.YogaClassOpeningRequests
            .Include(r => r.Instructor)
            .Include(r => r.YogaClassType)
            .Include(r => r.PreferredStudio)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<YogaClassOpeningRequest>> ListAsync(
        string? instructorUserId, YogaClassRequestStatus? status, CancellationToken cancellationToken = default) =>
        await Context.YogaClassOpeningRequests.AsNoTracking()
            .Include(r => r.Instructor)
            .Include(r => r.YogaClassType)
            .Include(r => r.PreferredStudio)
            .Where(r => instructorUserId == null || r.InstructorUserId == instructorUserId)
            .Where(r => status == null || r.RequestStatus == status)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

    public Task<int> CountPendingAsync(string instructorUserId, CancellationToken cancellationToken = default) =>
        Context.YogaClassOpeningRequests.CountAsync(
            r => r.InstructorUserId == instructorUserId && r.RequestStatus == YogaClassRequestStatus.Pending, cancellationToken);

    public Task LockAsync(int id, CancellationToken cancellationToken = default) =>
        AppLockAsync($"yoga-class-request-{id}", "This request is being processed. Please try again.", cancellationToken);
}
