using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MyVoucherService(IMyVoucherRepository repository, TimeProvider clock) : IMyVoucherService
{
    public async Task<Result<IReadOnlyList<MyVoucherResponse>>> ListAsync(string userId, string? orderNumber, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        decimal? subtotal = null;
        if (orderNumber is not null)
        {
            subtotal = await repository.GetCheckoutSubtotalAsync(userId, orderNumber, now, ct);
            if (subtotal is null)
                return Result<IReadOnlyList<MyVoucherResponse>>.Failure("Không tìm thấy đơn hàng có thể chọn voucher.", "ORDER_UNAVAILABLE", 404);
        }
        var wallet = await repository.ReadAsync(userId, now, ct);
        if (wallet is null)
            return Result<IReadOnlyList<MyVoucherResponse>>.Failure("Không tìm thấy tài khoản.", "ACCOUNT_NOT_FOUND", 404);
        var rows = new List<MyVoucherResponse>();
        foreach (var item in wallet.Vouchers)
        {
            var v = item.Definition;
            var c = item.Capacity;
            var check = VoucherCheckoutRules.Check(v, wallet.AccountCreatedAt, now, c);
            var owned = item.Awards.Count > 0 || c.PurchaserReserved + c.PurchaserConfirmed > 0;
            if (!check.IsSuccess && (!owned || subtotal.HasValue)) continue;
            var expiry = v.EndAt;
            if (v.Audience == VoucherAudience.NewCustomers && v.NewCustomerDuration is > 0 && v.NewCustomerUnit is { } unit)
            {
                var deadline = VoucherCheckoutRules.NewCustomerDeadlineUtc(wallet.AccountCreatedAt, v.NewCustomerDuration.Value, unit);
                if (deadline < expiry) expiry = deadline;
            }
            var remaining = Remaining(v.MaxRedemptions, c.Confirmed + c.Reserved);
            remaining = Smaller(remaining, Remaining(v.MaxRedemptionsPerUser, c.PurchaserConfirmed + c.PurchaserReserved));
            if (v.Audience == VoucherAudience.AssignedUsers) remaining = Smaller(remaining, c.GrantsAvailable);
            var status = check.IsSuccess ? MyVoucherStatus.Available
                : expiry <= now ? MyVoucherStatus.Expired
                : c.PurchaserReserved > 0 ? MyVoucherStatus.Reserved
                : c.PurchaserConfirmed > 0 && remaining == 0 ? MyVoucherStatus.Exhausted
                : v.StartAt > now ? MyVoucherStatus.Scheduled : MyVoucherStatus.Unavailable;
            var checkoutIssue = subtotal.HasValue ? VoucherCheckoutRules.Check(v, wallet.AccountCreatedAt, now, c, subtotal).ErrorMessage : null;
            // Calendar dates in Vietnam, including the seventh day.
            bool soon = status == MyVoucherStatus.Available && (expiry.AddHours(7).Date - now.AddHours(7).Date).Days <= 7;
            rows.Add(new(v.Id, v.Name, v.Code, (PromotionAudience)v.Audience,
                v.DiscountType == DiscountType.PERCENT ? PromotionDiscountType.Percentage : PromotionDiscountType.Fixed,
                v.DiscountValue, v.MaxDiscountAmount, v.MinimumSpend, Utc(v.StartAt), Utc(expiry), status, remaining,
                v.MaxRedemptionsPerUser, c.PurchaserReserved, soon, checkoutIssue, item.Awards, item.Uses));
        }
        return Result<IReadOnlyList<MyVoucherResponse>>.Success(rows.OrderByDescending(v => v.Audience == PromotionAudience.AssignedUsers)
            .ThenBy(v => v.ExpiresAt).ThenBy(v => v.Id).ToArray());
    }

    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);
    private static int? Remaining(int? limit, int used) => limit is { } n ? Math.Max(0, n - used) : null;
    private static int? Smaller(int? first, int? second) => first is null ? second : second is null ? first : Math.Min(first.Value, second.Value);
}
