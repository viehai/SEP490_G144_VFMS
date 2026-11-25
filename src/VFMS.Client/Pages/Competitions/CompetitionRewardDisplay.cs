using VFMS.Client.Components.Promotions;
using VFMS.Client.Services;
using VFMS.Contracts.Competitions;

namespace VFMS.Client.Pages.Competitions;

internal static class CompetitionRewardDisplay
{
    public static string Value(CompetitionRewardVoucher reward)
    {
        var discount = reward.DiscountType == "PERCENT" ? $"{Vnd.FormatAmount(reward.DiscountValue)}%" : Vnd.Format(reward.DiscountValue);
        return reward.MaximumDiscount is { } maximum ? $"Giảm {discount}, tối đa {Vnd.Format(maximum)}" : $"Giảm {discount}";
    }

    public static string Terms(CompetitionRewardVoucher reward)
    {
        var minimum = reward.MinimumSpend is { } spend ? $"Đơn từ {Vnd.Format(spend)}. " : "";
        return $"{minimum}Hiệu lực từ {Date(reward.StartAt)} đến {Date(reward.EndAt)} (UTC+7).";
    }

    private static string Date(DateTime utc) => PromotionFormModel.ToGymTime(utc).ToString("dd/MM/yyyy HH:mm");
}
