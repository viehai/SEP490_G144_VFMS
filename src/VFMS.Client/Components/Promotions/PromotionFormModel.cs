using VFMS.Contracts.Promotions;

namespace VFMS.Client.Components.Promotions;

/// <summary>Editable configuration. Dates are gym wall-clock timestamps (UTC+7), never browser-local time.</summary>
public sealed class PromotionFormModel
{
    private static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    public string? Name { get; set; }
    public string? Code { get; set; }
    public PromotionDiscountType? DiscountType { get; set; } = PromotionDiscountType.Percentage;
    public decimal? DiscountValue { get; set; }
    public decimal? MinimumSpend { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? MaxRedemptions { get; set; }
    public int? MaxRedemptionsPerUser { get; set; }
    public PromotionAudience Audience { get; set; } = PromotionAudience.AllCustomers;
    public int? NewCustomerDuration { get; set; }
    public PromotionDurationUnit? NewCustomerUnit { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsEnabled { get; set; } = true;

    public CreatePromotionRequest CreateRequest() => new(Name, Code, DiscountType, DiscountValue, MinimumSpend,
        PercentageCap, MaxRedemptions, ToUtc(StartAt), ToUtc(EndAt), IsEnabled)
        { Audience = Audience, NewCustomerDuration = NewCustomerDuration, NewCustomerUnit = NewCustomerUnit, MaxRedemptionsPerUser = MaxRedemptionsPerUser };

    public UpdatePromotionRequest UpdateRequest() => new(Name, Code, DiscountType, DiscountValue, MinimumSpend,
        PercentageCap, MaxRedemptions, ToUtc(StartAt), ToUtc(EndAt), IsEnabled)
        { Audience = Audience, NewCustomerDuration = NewCustomerDuration, NewCustomerUnit = NewCustomerUnit, MaxRedemptionsPerUser = MaxRedemptionsPerUser };

    public void ChangeAudience(PromotionAudience audience)
    {
        if (Audience == audience) return;
        Audience = audience;
        NewCustomerDuration = audience == PromotionAudience.NewCustomers ? 1 : null;
        NewCustomerUnit = audience == PromotionAudience.NewCustomers ? PromotionDurationUnit.Months : null;
    }

    public static string AudienceLabel(PromotionAudience audience) => audience switch
    {
        PromotionAudience.AllCustomers => "Tất cả khách hàng",
        PromotionAudience.NewCustomers => "Khách hàng mới",
        PromotionAudience.AssignedUsers => "Người được cấp voucher riêng",
        _ => "Chưa xác định"
    };

    private decimal? PercentageCap => DiscountType == PromotionDiscountType.Percentage ? MaxDiscountAmount : null;

    public static PromotionFormModel FromResponse(PromotionResponse response) => new()
    {
        Name = response.Name,
        Code = response.Code,
        DiscountType = response.DiscountType,
        DiscountValue = response.DiscountValue,
        MinimumSpend = response.MinimumSpend,
        MaxDiscountAmount = response.MaxDiscountAmount,
        MaxRedemptions = response.MaxRedemptions,
        MaxRedemptionsPerUser = response.MaxRedemptionsPerUser,
        Audience = response.Audience,
        NewCustomerDuration = response.NewCustomerDuration,
        NewCustomerUnit = response.NewCustomerUnit,
        StartAt = ToGymTime(response.StartAt),
        EndAt = ToGymTime(response.EndAt),
        IsEnabled = response.IsEnabled
    };

    public static DateTime ToGymTime(DateTime utc) => DateTime.SpecifyKind(utc.Add(GymOffset), DateTimeKind.Unspecified);

    private static DateTime? ToUtc(DateTime? gymTime) => gymTime is { } value && value >= DateTime.MinValue.Add(GymOffset)
        ? DateTime.SpecifyKind(value.Subtract(GymOffset), DateTimeKind.Utc) : null;
}
