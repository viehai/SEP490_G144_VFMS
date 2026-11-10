using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

/// <summary>Module 6 – Competitions (UC-56..UC-62).</summary>
public sealed class CompetitionRepository : AuditedRepositoryBase, ICompetitionRepository
{
    public CompetitionRepository(VfmsDbContext context) : base(context) { }

    // ── Competitions ─────────────────────────────────────────────

    public async Task<(IReadOnlyList<Competition> Items, int TotalCount)> SearchCompetitionsAsync(
        CompetitionStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.Competitions.AsNoTracking().AsQueryable();
        if (status.HasValue)
            query = query.Where(c => c.Status == status);
        if (search is not null)
            query = query.Where(c => c.CompetitionName.Contains(search) ||
                                     (c.Description != null && c.Description.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.StartDate).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Competition?> GetCompetitionAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Competitions.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Competition?> GetCompetitionWithDetailsAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Competitions
            .Include(c => c.Prizes).ThenInclude(p => p.Voucher)
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> CompetitionNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default) =>
        Context.Competitions.AnyAsync(
            c => c.CompetitionName.ToLower() == name.ToLower() && (excludingId == null || c.Id != excludingId),
            cancellationToken);

    public Task<int> CountActiveParticipantsAsync(int competitionId, CancellationToken cancellationToken = default) =>
        Context.CompetitionJoins.CountAsync(
            j => j.CompetitionId == competitionId && j.Status != CompetitionJoinStatus.Disqualified, cancellationToken);

    // ── Participants ─────────────────────────────────────────────

    public async Task<(IReadOnlyList<CompetitionJoin> Items, int TotalCount)> SearchParticipantsAsync(
        int? competitionId, CompetitionJoinStatus? status, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = ParticipantsWithDetails().AsNoTracking();
        if (competitionId.HasValue)
            query = query.Where(j => j.CompetitionId == competitionId);
        if (status.HasValue)
            query = query.Where(j => j.Status == status);
        if (search is not null)
            query = query.Where(j => j.Competition.CompetitionName.Contains(search) ||
                                     j.Member.FullName.Contains(search));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.RegisteredAt).ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<CompetitionJoin?> GetParticipantAsync(int id, CancellationToken cancellationToken = default) =>
        ParticipantsWithDetails().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<CompetitionJoin?> GetParticipantByMemberAsync(int competitionId, string memberUserId, CancellationToken cancellationToken = default) =>
        ParticipantsWithDetails().FirstOrDefaultAsync(j => j.CompetitionId == competitionId && j.MemberUserId == memberUserId, cancellationToken);

    public Task<bool> IsParticipantAsync(int competitionId, string memberUserId, CancellationToken cancellationToken = default) =>
        Context.CompetitionJoins.AnyAsync(j => j.CompetitionId == competitionId && j.MemberUserId == memberUserId, cancellationToken);

    public async Task<(IReadOnlyList<CompetitionJoin> Items, int TotalCount)> SearchMemberRegistrationsAsync(
        string memberUserId, int? competitionId, CompetitionJoinStatus? status,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = ParticipantsWithDetails().AsNoTracking().Where(j => j.MemberUserId == memberUserId);
        if (competitionId.HasValue)
            query = query.Where(j => j.CompetitionId == competitionId);
        if (status.HasValue)
            query = query.Where(j => j.Status == status);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.RegisteredAt).ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // ── Prizes ───────────────────────────────────────────────────

    public Task<CompetitionPrize?> GetPrizeForRankAsync(int competitionId, int rank, CancellationToken cancellationToken = default) =>
        Context.CompetitionPrizes.FirstOrDefaultAsync(p => p.CompetitionId == competitionId && p.Rank == rank, cancellationToken);

    public Task<Voucher?> GetVoucherAsync(int voucherId, CancellationToken cancellationToken = default) =>
        Context.Vouchers.FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken);

    private IQueryable<CompetitionJoin> ParticipantsWithDetails() =>
        Context.CompetitionJoins
            .Include(j => j.Competition)
            .Include(j => j.Member)
            .Include(j => j.ConfirmedByUser);
}
