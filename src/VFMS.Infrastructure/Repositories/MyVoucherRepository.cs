using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MyVoucherRepository(VfmsDbContext db) : IMyVoucherRepository
{
    public async Task<VoucherWalletReadModel?> ReadAsync(string userId, DateTime now, CancellationToken ct)
    {
        var created = await db.ApplicationUsers.Where(u => u.UserId == userId && !u.IsDeleted)
            .Select(u => (DateTime?)u.CreatedAt).SingleOrDefaultAsync(ct);
        if (created is null) return null;

        var awards = await db.VoucherGrants.AsNoTracking().Where(g => g.UserId == userId)
            .OrderBy(g => g.Id).Select(g => new { g.VoucherId, Award = new VoucherAwardResponse(
                g.CompetitionPrize.Competition.CompetitionName, g.CompetitionPrize.Rank, g.CreatedAt) }).ToListAsync(ct);
        var own = db.VoucherRedemptions.AsNoTracking().Where(r => r.Order.PurchaserUserId == userId &&
            r.RedemptionStatus != VoucherRedemptionStatus.Released);
        var rawUses = await own.Where(r => r.RedemptionStatus == VoucherRedemptionStatus.Confirmed)
            .OrderByDescending(r => r.RedeemedAt).Select(r => new
            {
                r.VoucherId,
                r.Order.OrderNumber,
                r.DiscountAmount,
                UsedAt = r.Order.CompletedAt ?? r.RedeemedAt,
                CodeSnapshot = r.VoucherCodeSnapshot,
                VoucherCode = r.Voucher.Code,
                NameSnapshot = r.VoucherNameSnapshot,
                VoucherName = r.Voucher.Name
            }).ToListAsync(ct);
        var uses = rawUses.Select(r => new
        {
            r.VoucherId,
            Use = new VoucherUseResponse(
                r.OrderNumber, r.DiscountAmount, r.UsedAt,
                r.CodeSnapshot ?? r.VoucherCode, r.NameSnapshot ?? r.VoucherName)
        }).ToList();

        var definitions = await db.Vouchers.AsNoTracking().Where(v => !v.IsDeleted &&
            (v.Audience == VoucherAudience.AssignedUsers ? v.Grants.Any(g => g.UserId == userId) :
                v.VoucherStatus != VoucherStatus.Inactive && v.StartAt <= now && v.EndAt > now ||
                own.Any(r => r.VoucherId == v.Id))).ToListAsync(ct);
        var capacities = await VoucherCapacityReader.ReadForUserAsync(db, definitions.Select(v => v.Id).ToArray(), userId, ct);
        var awardIndex = awards.ToLookup(a => a.VoucherId, a => a.Award);
        var useIndex = uses.ToLookup(u => u.VoucherId, u => u.Use);
        return new(created.Value, definitions.Select(v => new WalletVoucher(v, capacities[v.Id],
            awardIndex[v.Id].ToArray(), useIndex[v.Id].ToArray())).ToArray());
    }

    public async Task<decimal?> GetCheckoutSubtotalAsync(string userId, string orderNumber, DateTime now, CancellationToken ct)
    {
        var ptCutoff = now.AddMinutes(-PackageOrderTiming.PtChoosePaymentMinutes);
        var onlineCutoff = now.AddMinutes(-PackageOrderTiming.OnlinePaymentMinutes);
        var cashCutoff = now.AddHours(-PackageOrderTiming.CounterPaymentHours);
        var orders = db.Orders.Where(o => o.PurchaserUserId == userId && o.OrderNumber == orderNumber &&
            o.OrderStatus == OrderStatus.Pending && (o.VoucherRedemption == null ||
            o.VoucherRedemption.RedemptionStatus == VoucherRedemptionStatus.Released));
        var lockerFee = await orders.Where(o => o.PaymentTransaction != null && o.PaymentTransaction.LockerRentalContractId != null &&
                o.PaymentTransaction.PaymentStatus == PaymentStatus.Pending && o.PaymentTransaction.PaymentStartedAt == null &&
                (o.PaymentTransaction.GatewayName == "PendingChoice" || o.PaymentTransaction.LockerPurpose == LockerPaymentPurpose.Rental && o.Channel == OrderChannel.Online) &&
                o.PaymentTransaction.PaymentExpiresAt > now && o.PaymentTransaction.HoldExpiresAt > now &&
                (o.PaymentTransaction.LockerPurpose == LockerPaymentPurpose.Rental || o.PaymentTransaction.LockerPurpose == LockerPaymentPurpose.Renewal))
            .Select(o => (decimal?)(o.PaymentTransaction!.LockerPurpose == LockerPaymentPurpose.Rental
                ? o.PaymentTransaction.LockerRentalContract!.RentalPrice
                : o.PaymentTransaction.AmountDue - (o.PaymentTransaction.OverdueFees ?? 0))).SingleOrDefaultAsync(ct);
        if (lockerFee is > 0) return lockerFee;
        var prices = await orders.Where(o => db.PTPurchases.Any(p => p.OrderId == o.Id)
            ? o.Channel == OrderChannel.Online && o.PaymentTransaction == null && o.CreatedAt > ptCutoff &&
                db.PTPurchases.Any(p => p.OrderId == o.Id && p.Status == PTPurchaseStatus.PendingPayment)
            : o.PaymentTransaction != null && o.PaymentTransaction.PaymentStatus == PaymentStatus.Pending &&
                (o.PaymentTransaction.PaymentMethod == PaymentMethod.Cash || o.PaymentTransaction.PaymentMethod == PaymentMethod.VietQR) &&
                (o.Channel == OrderChannel.Online ? o.CreatedAt > onlineCutoff : o.CreatedAt > cashCutoff))
            .SelectMany(o => o.Items.Select(i => i.UnitPriceSnapshot)).ToListAsync(ct);
        return prices.Count > 0 ? prices.Sum() : null;
    }
}
