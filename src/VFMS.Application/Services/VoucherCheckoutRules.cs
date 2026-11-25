using System.Globalization;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public static class VoucherCheckoutRules
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public static DateTime NewCustomerDeadlineUtc(DateTime createdAtUtc, int duration, FreezeTierUnit unit)
    {
        var created = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        var local = created.AddHours(7);
        // Clamp durations that extend beyond the representable calendar.
        if (unit == FreezeTierUnit.Months && duration > (9999 - local.Year) * 12 + 12 - local.Month ||
            unit == FreezeTierUnit.Days && duration > (DateTime.MaxValue - local).TotalDays)
            return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var deadline = unit == FreezeTierUnit.Months ? local.AddMonths(duration) : local.AddDays(duration);
        return deadline.AddHours(-7);
    }

    public static Result<decimal> Check(Voucher voucher, DateTime accountCreatedAtUtc, DateTime nowUtc,
        VoucherCapacity capacity, decimal? subtotal = null)
    {
        if (voucher.IsDeleted || !VoucherRules.IsEnabled(voucher.VoucherStatus))
            return Result<decimal>.Failure("Voucher đã bị tắt.", "VOUCHER_DISABLED");
        if (nowUtc >= voucher.EndAt)
            return Result<decimal>.Failure("Voucher đã hết hạn.", "VOUCHER_EXPIRED");
        if (nowUtc < voucher.StartAt)
            return Result<decimal>.Failure("Voucher chưa đến thời gian sử dụng.", "VOUCHER_SCHEDULED");
        if (voucher.Audience == VoucherAudience.AssignedUsers && capacity.AvailableGrantId is null)
            return Result<decimal>.Failure("Tài khoản của bạn không có lượt voucher riêng khả dụng.", "VOUCHER_GRANT_UNAVAILABLE", 409);
        if (voucher.Audience == VoucherAudience.NewCustomers &&
            (voucher.NewCustomerDuration is not > 0 || voucher.NewCustomerUnit is not (FreezeTierUnit.Days or FreezeTierUnit.Months) ||
             nowUtc < accountCreatedAtUtc || nowUtc >= NewCustomerDeadlineUtc(accountCreatedAtUtc, voucher.NewCustomerDuration.Value, voucher.NewCustomerUnit.Value)))
            return Result<decimal>.Failure("Tài khoản nằm ngoài thời gian áp dụng ưu đãi khách hàng mới.", "VOUCHER_ACCOUNT_AGE");
        if (voucher.MaxRedemptions is { } limit)
        {
            if (capacity.Confirmed >= limit)
                return Result<decimal>.Failure("Voucher đã hết lượt sử dụng.", "VOUCHER_QUOTA_EXHAUSTED", 409);
            if ((long)capacity.Confirmed + capacity.Reserved >= limit)
                return Result<decimal>.Failure("Các lượt voucher còn lại đang được giữ cho giao dịch khác. Vui lòng thử lại sau.", "VOUCHER_CAPACITY_UNAVAILABLE", 409);
        }
        if (voucher.MaxRedemptionsPerUser is { } perUser && (long)capacity.PurchaserConfirmed + capacity.PurchaserReserved >= perUser)
            return Result<decimal>.Failure("Bạn đã dùng hoặc đang giữ hết số lượt voucher dành cho tài khoản.", "VOUCHER_ACCOUNT_LIMIT", 409);
        if (!Enum.IsDefined(voucher.Audience) || voucher.DiscountValue <= 0 || voucher.MinimumSpend < 0 ||
            voucher.DiscountType is not (DiscountType.FIXED or DiscountType.PERCENT) ||
            voucher.DiscountType == DiscountType.PERCENT && (voucher.DiscountValue > 100 || voucher.MaxDiscountAmount is not > 0))
            return Result<decimal>.Failure("Voucher chưa có cấu hình giảm giá hợp lệ.", "VOUCHER_CONFIGURATION_INVALID");
        if (subtotal is not { } price)
            return Result<decimal>.Success(0);
        if (price <= 0 || price > 9_999_999_999_999_999m || decimal.Truncate(price) != price)
            return Result<decimal>.Failure("Giá trị đơn hàng không hợp lệ.", "ORDER_AMOUNT_INVALID");
        if (price < voucher.MinimumSpend)
            return Result<decimal>.Failure($"Đơn hàng cần tối thiểu {voucher.MinimumSpend.Value.ToString("N0", Vietnamese)} đ.", "MSG-22");
        var discount = voucher.DiscountType == DiscountType.FIXED ? voucher.DiscountValue :
            Math.Min(price / 100m * voucher.DiscountValue, voucher.MaxDiscountAmount!.Value);
        return Result<decimal>.Success(decimal.Truncate(Math.Min(price, discount)));
    }
}
