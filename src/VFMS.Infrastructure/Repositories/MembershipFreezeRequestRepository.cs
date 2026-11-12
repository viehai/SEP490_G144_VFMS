using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipFreezeRequestRepository(VfmsDbContext context)
    : AuditedRepositoryBase(context), IMembershipFreezeRequestRepository
{
    public Task<MemberMembership?> GetOwnedMembershipAsync(
        string memberId, int membershipId, bool forUpdate, CancellationToken ct)
    {
        // All Member mutations take this parent lock before checking request state and overlaps.
        var memberships = forUpdate && Context.Database.IsSqlServer()
            ? Context.MemberMemberships.FromSqlRaw("SELECT * FROM [MemberMemberships] WITH (UPDLOCK, HOLDLOCK)")
            : Context.MemberMemberships;
        if (!forUpdate)
            memberships = memberships.AsNoTracking();

        return memberships.Where(membership => membership.Id == membershipId && membership.MemberUserId == memberId)
            .Include(membership => membership.OrderItem)
            .Include(membership => membership.PackageComponent)
            .Include(membership => membership.MembershipFreezes)
            .SingleOrDefaultAsync(ct);
    }

    public Task<List<MembershipFreezeReadModel>> ListOwnedRequestsAsync(string memberId, CancellationToken ct) =>
        Project(OwnedRequests(memberId).OrderByDescending(request => request.CreatedAt)
            .ThenByDescending(request => request.Id)).ToListAsync(ct);

    public Task<MembershipFreezeReadModel?> FindOwnedRequestAsync(string memberId, int requestId, CancellationToken ct) =>
        Project(OwnedRequests(memberId).Where(request => request.Id == requestId)).SingleOrDefaultAsync(ct);

    private IQueryable<MembershipFreeze> OwnedRequests(string memberId) =>
        Context.MembershipFreezes.AsNoTracking()
            .Where(request => request.RequestedByUserId == memberId &&
                request.MemberMembership.MemberUserId == memberId);

    private static IQueryable<MembershipFreezeReadModel> Project(IQueryable<MembershipFreeze> requests) =>
        requests.Select(request => new MembershipFreezeReadModel(request,
                request.MemberMembership.OrderItem.PackageNameSnapshot, request.MemberMembership.MembershipStatus));
}
