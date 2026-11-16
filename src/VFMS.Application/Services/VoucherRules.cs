using VFMS.Contracts.Promotions;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>Canonical voucher identity and permanent lifecycle shared by promotion administration and checkout.</summary>
internal static class VoucherRules
{
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    public static bool IsEnabled(VoucherStatus status) => status != VoucherStatus.Inactive;
    public static PromotionStatus EffectiveStatus(
        VoucherStatus status, DateTime startAt, DateTime endAt, int? limit, int confirmed, DateTime now) =>
        !IsEnabled(status) ? PromotionStatus.Disabled
        : now >= endAt || limit is { } maximum && confirmed >= maximum ? PromotionStatus.Expired
        : now < startAt ? PromotionStatus.Scheduled : PromotionStatus.Active;
}
