using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipFreezeReviewRepository(VfmsDbContext context)
    : AuditedRepositoryBase(context), IMembershipFreezeReviewRepository
{
    public async Task<(IReadOnlyList<MembershipFreezeReviewReadModel> Items, int TotalCount)> SearchAsync(
        string? type, int page, int pageSize, CancellationToken ct)
    {
        var query = Context.MembershipFreezes.AsNoTracking().Where(request =>
            ((type == null || type == "" || type == MembershipFreezeReviewTypes.Freeze) &&
                request.RequestStatus == FreezeRequestStatus.Pending) ||
            ((type == null || type == "" || type == MembershipFreezeReviewTypes.EarlyUnfreeze) &&
                request.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending));
        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderBy(request =>
                request.EarlyUnfreezeRequestStatus == FreezeRequestStatus.Pending ? request.EarlyUnfreezeRequestedAt : request.CreatedAt)
            .ThenBy(request => request.Id).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return (items, total);
    }

    public Task<MembershipFreezeReviewReadModel?> FindAsync(int requestId, bool early, CancellationToken ct) =>
        Project(Context.MembershipFreezes.AsNoTracking().Where(request =>
            request.Id == requestId && (!early || request.EarlyUnfreezeRequestedAt != null))).SingleOrDefaultAsync(ct);

    private static IQueryable<MembershipFreezeReviewReadModel> Project(IQueryable<MembershipFreeze> requests) =>
        requests.Select(request => new MembershipFreezeReviewReadModel(request,
            request.MemberMembership.Member.FullName, request.MemberMembership.Member.Email,
            request.MemberMembership.OrderItem.PackageNameSnapshot, request.MemberMembership));
}
