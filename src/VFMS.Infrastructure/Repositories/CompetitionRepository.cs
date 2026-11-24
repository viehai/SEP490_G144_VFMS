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
        CompetitionStatus? status, string? search, int page, int pageSize, bool includeDrafts, CancellationToken cancellationToken = default)
    {
        var query = Context.Competitions.AsNoTracking().AsQueryable();
        if (!includeDrafts)
            query = query.Where(c => c.Status != CompetitionStatus.Draft);
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

    public async Task<T> WithResultsLockAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(ct);
        // Configuration, staff scoring and UC-66 confirmation share one short write lock.
        await AppLockAsync("VFMS.CompetitionResults", "Hệ thống đang cập nhật kết quả cuộc thi. Vui lòng thử lại.", ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    public Task<Voucher?> LockRewardVoucherAsync(int voucherId, CancellationToken ct)
    {
        var rows = Context.Database.IsSqlServer()
            ? Context.Vouchers.FromSqlInterpolated($"SELECT * FROM [Vouchers] WITH (XLOCK, HOLDLOCK) WHERE [Id] = {voucherId}")
            : Context.Vouchers;
        return rows.AsNoTracking().SingleOrDefaultAsync(v => v.Id == voucherId, ct);
    }

    public Task<List<Voucher>> RewardVoucherOptionsAsync(DateTime at, CancellationToken ct) =>
        Context.Vouchers.AsNoTracking().Where(v => v.Audience == VoucherAudience.AssignedUsers &&
            v.VoucherStatus == VoucherStatus.Active && v.StartAt <= at && v.EndAt > at)
            .OrderBy(v => v.Name).ThenBy(v => v.Id).ToListAsync(ct);

    public Task<int> CountCommittedAsync(int voucherId, int? excludingCompetitionId, CancellationToken ct) =>
        CompetitionRewardCapacity.CommittedAsync(Context, voucherId, excludingCompetitionId, ct);

    public async Task<int> CountUserCommitmentsAsync(int voucherId, string userId, CancellationToken ct) =>
        await Context.VoucherGrants.IgnoreQueryFilters().CountAsync(g => g.VoucherId == voucherId && g.UserId == userId, ct) +
        await Context.VoucherRedemptions.IgnoreQueryFilters().CountAsync(r => r.VoucherId == voucherId &&
            r.VoucherGrantId == null && r.Order.PurchaserUserId == userId && r.RedemptionStatus != VoucherRedemptionStatus.Released, ct);

    public Task<List<CompetitionJoin>> GetFinalistsAsync(int competitionId, CancellationToken ct) =>
        ParticipantsWithDetails().Where(j => j.CompetitionId == competitionId && j.Status != CompetitionJoinStatus.Disqualified)
            .OrderBy(j => j.Id).ToListAsync(ct);

    public Task<Dictionary<int,string>> GetMemberAwardsAsync(string userId, int[] competitionIds, CancellationToken ct) =>
        Context.VoucherGrants.AsNoTracking().Where(g => g.UserId == userId && competitionIds.Contains(g.CompetitionPrize.CompetitionId))
            .ToDictionaryAsync(g => g.CompetitionPrize.CompetitionId, g => g.Voucher.Name, ct);

    private IQueryable<CompetitionJoin> ParticipantsWithDetails() =>
        Context.CompetitionJoins
            .Include(j => j.Competition)
            .Include(j => j.Member)
            .Include(j => j.ConfirmedByUser);
}
