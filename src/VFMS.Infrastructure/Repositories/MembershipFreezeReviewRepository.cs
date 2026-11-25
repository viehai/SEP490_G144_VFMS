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
        MembershipFreezeReviewListRequest request, CancellationToken ct)
    {
        var query = Context.MembershipFreezes.AsNoTracking();
        if (request.Search?.Trim() is { Length: > 0 } search)
            query = query.Where(freeze => freeze.MemberMembership.Member.FullName.Contains(search) ||
                (freeze.MemberMembership.Member.Email != null && freeze.MemberMembership.Member.Email.Contains(search)));
        if (request.FromDate is { } from)
            query = query.Where(freeze => freeze.RequestedEndDate >= from);
        if (request.ToDate is { } to)
            query = query.Where(freeze => freeze.RequestedStartDate <= to);

        var freezes = query.Select(freeze => new
        {
            freeze.Id, Early = false, Status = freeze.RequestStatus, SubmittedAt = freeze.CreatedAt
        });
        var early = query.Where(freeze => freeze.EarlyUnfreezeRequestedAt != null && freeze.EarlyUnfreezeRequestStatus != null)
            .Select(freeze => new
            {
                freeze.Id, Early = true, Status = freeze.EarlyUnfreezeRequestStatus!.Value,
                SubmittedAt = freeze.EarlyUnfreezeRequestedAt!.Value
            });
        var events = request.Type switch
        {
            MembershipFreezeReviewTypes.Freeze => freezes,
            MembershipFreezeReviewTypes.EarlyUnfreeze => early,
            _ => freezes.Concat(early)
        };
        if (request.Status == "History")
            events = events.Where(item => item.Status == FreezeRequestStatus.Completed || item.Status == FreezeRequestStatus.Rejected ||
                item.Status == FreezeRequestStatus.Cancelled || item.Status == FreezeRequestStatus.Expired ||
                (item.Early && item.Status == FreezeRequestStatus.Approved));
        else if (string.IsNullOrEmpty(request.Status))
            events = events.Where(item => item.Status == FreezeRequestStatus.Pending);
        else if (Enum.TryParse<FreezeRequestStatus>(request.Status, out var status))
            events = events.Where(item => item.Status == status);

        var total = await events.CountAsync(ct);
        var page = await events.OrderByDescending(item => item.Status == FreezeRequestStatus.Pending)
            .ThenByDescending(item => item.SubmittedAt).ThenByDescending(item => item.Id).ThenBy(item => item.Early)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var ids = page.Select(item => item.Id).Distinct().ToArray();
        var rows = await Project(query.Where(freeze => ids.Contains(freeze.Id)))
            .ToDictionaryAsync(row => row.Request.Id, ct);
        var items = page.Where(item => rows.ContainsKey(item.Id))
            .Select(item => rows[item.Id] with { EarlyUnfreeze = item.Early }).ToArray();
        return (items, total);
    }

    public Task<MembershipFreezeReviewReadModel?> FindAsync(int requestId, bool early, CancellationToken ct) =>
        Project(Context.MembershipFreezes.AsNoTracking().Where(request =>
            request.Id == requestId && (!early || request.EarlyUnfreezeRequestedAt != null))).SingleOrDefaultAsync(ct);

    private static IQueryable<MembershipFreezeReviewReadModel> Project(IQueryable<MembershipFreeze> requests) =>
        requests.Select(request => new MembershipFreezeReviewReadModel(request,
            request.MemberMembership.Member.FullName, request.MemberMembership.Member.Email,
            request.MemberMembership.OrderItem.PackageNameSnapshot, request.MemberMembership, false));
}
