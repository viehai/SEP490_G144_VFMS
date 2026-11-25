using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

internal static class CompetitionRewardCapacity
{
    // A published rank commits one slot. Its grant replaces that slot and continues
    // to occupy it throughout reservation and redemption.
    internal static async Task<int> CommittedAsync(VfmsDbContext db, int voucherId, int? excludeCompetitionId, CancellationToken ct)
    {
        var grants = db.VoucherGrants.IgnoreQueryFilters();
        var granted = await grants.CountAsync(g => g.VoucherId == voucherId, ct);
        var independent = await db.VoucherRedemptions.IgnoreQueryFilters().CountAsync(r => r.VoucherId == voucherId &&
            r.VoucherGrantId == null && r.RedemptionStatus != VoucherRedemptionStatus.Released, ct);
        var promised = await db.CompetitionPrizes.CountAsync(p => p.VoucherId == voucherId &&
            (excludeCompetitionId == null || p.CompetitionId != excludeCompetitionId) &&
            (p.Competition.Status == CompetitionStatus.Published || p.Competition.Status == CompetitionStatus.Ongoing ||
             p.Competition.Status == CompetitionStatus.Completed) && !grants.Any(g => g.CompetitionPrizeId == p.Id), ct);
        return granted + independent + promised;
    }
}
