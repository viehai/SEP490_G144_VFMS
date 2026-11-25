using System.Text.Json.Serialization;

namespace VFMS.Contracts.Promotions;

[JsonConverter(typeof(JsonStringEnumConverter<PromotionDiscountType>))]
public enum PromotionDiscountType { Percentage, Fixed }

[JsonConverter(typeof(JsonStringEnumConverter<PromotionStatus>))]
public enum PromotionStatus { Scheduled, Active, Expired, Disabled }

[JsonConverter(typeof(JsonStringEnumConverter<PromotionAudience>))]
public enum PromotionAudience { AllCustomers, NewCustomers, AssignedUsers }

[JsonConverter(typeof(JsonStringEnumConverter<PromotionDurationUnit>))]
public enum PromotionDurationUnit { Days, Months }

/// <summary>Configuration only. Dates are UTC timestamps; a null global quota means unlimited usage.</summary>
public abstract record PromotionRequest(
    string? Name, string? Code, PromotionDiscountType? DiscountType, decimal? DiscountValue,
    decimal? MinimumSpend, decimal? MaxDiscountAmount, int? MaxRedemptions,
    DateTime? StartAt, DateTime? EndAt, bool? IsEnabled)
{
    // Older clients omit the audience configuration when editing an existing voucher.
    public PromotionAudience? Audience { get; init; }
    public int? NewCustomerDuration { get; init; }
    public PromotionDurationUnit? NewCustomerUnit { get; init; }
    public int? MaxRedemptionsPerUser { get; init; }
}

public sealed record CreatePromotionRequest(
    string? Name, string? Code, PromotionDiscountType? DiscountType, decimal? DiscountValue,
    decimal? MinimumSpend, decimal? MaxDiscountAmount, int? MaxRedemptions,
    DateTime? StartAt, DateTime? EndAt, bool? IsEnabled)
    : PromotionRequest(Name, Code, DiscountType, DiscountValue, MinimumSpend, MaxDiscountAmount,
        MaxRedemptions, StartAt, EndAt, IsEnabled);

public sealed record UpdatePromotionRequest(
    string? Name, string? Code, PromotionDiscountType? DiscountType, decimal? DiscountValue,
    decimal? MinimumSpend, decimal? MaxDiscountAmount, int? MaxRedemptions,
    DateTime? StartAt, DateTime? EndAt, bool? IsEnabled)
    : PromotionRequest(Name, Code, DiscountType, DiscountValue, MinimumSpend, MaxDiscountAmount,
        MaxRedemptions, StartAt, EndAt, IsEnabled);

public sealed record SetPromotionEnabledRequest(bool? IsEnabled);

public sealed record PromotionResponse(
    int Id, string Name, string Code, PromotionDiscountType DiscountType, decimal DiscountValue,
    decimal? MinimumSpend, decimal? MaxDiscountAmount, int? MaxRedemptions, int UsedCount,
    DateTime StartAt, DateTime EndAt, bool IsEnabled, PromotionStatus Status,
    PromotionAudience Audience = PromotionAudience.AllCustomers, int? MaxRedemptionsPerUser = null,
    int? NewCustomerDuration = null, PromotionDurationUnit? NewCustomerUnit = null, int GrantedCount = 0);
