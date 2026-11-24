using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public static class VoucherCapacityReader
{
    public static async Task<Dictionary<int, VoucherCapacity>> ReadForUserAsync(VfmsDbContext db,
        int[] voucherIds, string purchaserId, CancellationToken ct)
    {
        var result = voucherIds.Distinct().ToDictionary(id => id, _ => new VoucherCapacity());
        if (result.Count == 0) return result;
        // JOB-01 releases expired holds. Count each financial reservation once, even for gifted packages.
        var committed = db.VoucherRedemptions.IgnoreQueryFilters().Where(r => voucherIds.Contains(r.VoucherId) &&
            (r.RedemptionStatus == VoucherRedemptionStatus.Reserved || r.RedemptionStatus == VoucherRedemptionStatus.Confirmed));
        var totals = await committed.GroupBy(r => r.VoucherId).Select(g => new
        {
            Id = g.Key,
            Confirmed = g.Count(r => r.RedemptionStatus == VoucherRedemptionStatus.Confirmed),
            Reserved = g.Count(r => r.RedemptionStatus == VoucherRedemptionStatus.Reserved),
            OwnConfirmed = g.Count(r => r.RedemptionStatus == VoucherRedemptionStatus.Confirmed && r.Order.PurchaserUserId == purchaserId),
            OwnReserved = g.Count(r => r.RedemptionStatus == VoucherRedemptionStatus.Reserved && r.Order.PurchaserUserId == purchaserId)
        }).ToListAsync(ct);
        foreach (var total in totals)
            result[total.Id] = new(total.Confirmed, total.Reserved, total.OwnConfirmed, total.OwnReserved);

        var grants = await db.Set<VoucherGrant>().Where(g => !g.IsDeleted && voucherIds.Contains(g.VoucherId) && g.UserId == purchaserId &&
                !committed.Any(r => r.VoucherGrantId == g.Id)).GroupBy(g => g.VoucherId)
            .Select(g => new { Id = g.Key, FirstId = g.Min(x => x.Id), Available = g.Count() }).ToListAsync(ct);
        foreach (var grant in grants)
            result[grant.Id] = result[grant.Id] with { GrantsAvailable = grant.Available, AvailableGrantId = grant.FirstId };
        return result;
    }
}
