using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class PurchaseProposalRepository : AuditedRepositoryBase, IPurchaseProposalRepository
{
    public PurchaseProposalRepository(VfmsDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<PurchaseProposal> Items, int TotalCount)> SearchAsync(
        string? proposedByUserId, PurchaseProposalStatus? status, bool excludeDrafts, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.PurchaseProposals.AsNoTracking().AsQueryable();
        if (proposedByUserId is not null)
            query = query.Where(p => p.ProposedByUserId == proposedByUserId);
        if (excludeDrafts)
            query = query.Where(p => p.ProposalStatus != PurchaseProposalStatus.Draft);
        if (status.HasValue)
            query = query.Where(p => p.ProposalStatus == status);
        if (search is not null)
            query = query.Where(p => p.ProposalNumber.Contains(search) ||
                                     p.ProposedByUser.FullName.Contains(search) ||
                                     (p.Description != null && p.Description.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var items = await WithDetails(query)
            .OrderBy(p => p.ProposalStatus == PurchaseProposalStatus.Submitted ? 0 : 1) // pending decisions first
            .ThenByDescending(p => p.SubmittedAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<PurchaseProposal?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        WithDetails(Context.PurchaseProposals).AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> ProposalNumberExistsAsync(string proposalNumber, CancellationToken cancellationToken = default) =>
        Context.PurchaseProposals.IgnoreQueryFilters().AnyAsync(p => p.ProposalNumber == proposalNumber, cancellationToken);

    public async Task<IReadOnlySet<int>> GetExistingEquipmentIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        (await Context.Equipments.Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken)).ToHashSet();

    private static IQueryable<PurchaseProposal> WithDetails(IQueryable<PurchaseProposal> query) =>
        query
            .Include(p => p.ProposedByUser)
            .Include(p => p.Items).ThenInclude(i => i.Equipment)
            .Include(p => p.Reviews).ThenInclude(r => r.ReviewedByUser);
}
