using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipAccessRepository(VfmsDbContext context) : IMembershipAccessRepository
{
    public Task<OwnedMembershipAccess?> FindOwnedAsync(string memberId, int membershipId, CancellationToken ct) =>
        context.MemberMemberships.AsNoTracking()
            .Where(membership => membership.Id == membershipId && membership.MemberUserId == memberId)
            .Select(membership => new OwnedMembershipAccess(membership.Id, membership.PackageComponent.ComponentType,
                membership.MembershipStatus, membership.StartDate, membership.EndDate)).SingleOrDefaultAsync(ct);
}
