using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipFreezeLifecycleRepository(VfmsDbContext context)
    : AuditedRepositoryBase(context), IMembershipFreezeLifecycleRepository
{
    public async Task<IReadOnlyList<int>> ListDueAsync(DateOnly today, int afterId, int pageSize, CancellationToken ct) =>
        await Context.MembershipFreezes.AsNoTracking().Where(freeze => freeze.Id > afterId &&
                ((freeze.RequestStatus == FreezeRequestStatus.Pending && freeze.RequestedStartDate < today) ||
                 (freeze.RequestStatus == FreezeRequestStatus.Approved && freeze.RequestedStartDate <= today) ||
                 (freeze.RequestStatus == FreezeRequestStatus.Active && freeze.RequestedEndDate < today)))
            .OrderBy(freeze => freeze.Id).Select(freeze => freeze.Id).Take(pageSize).ToListAsync(ct);

    public Task<MembershipFreezeLifecycleTarget?> FindAsync(int requestId, CancellationToken ct) =>
        Context.MembershipFreezes.AsNoTracking().Where(freeze => freeze.Id == requestId)
            .Select(freeze => new MembershipFreezeLifecycleTarget(
                freeze.Id, freeze.MemberMembershipId, freeze.MemberMembership.MemberUserId)).SingleOrDefaultAsync(ct);
}
