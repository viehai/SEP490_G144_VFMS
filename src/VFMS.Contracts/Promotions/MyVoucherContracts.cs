using System.Text.Json.Serialization;

namespace VFMS.Contracts.Promotions;

[JsonConverter(typeof(JsonStringEnumConverter<MyVoucherStatus>))]
public enum MyVoucherStatus { Available, Reserved, Exhausted, Expired, Scheduled, Unavailable }

public sealed record VoucherAwardResponse(string Competition, int Rank, DateTime GrantedAt);
public sealed record VoucherUseResponse(string OrderNumber, decimal Discount, DateTime UsedAt, string Code, string Name);

public sealed record MyVoucherResponse(
    int Id, string Name, string Code, PromotionAudience Audience,
    PromotionDiscountType DiscountType, decimal DiscountValue, decimal? MaximumDiscount, decimal? MinimumSpend,
    DateTime StartsAt, DateTime ExpiresAt, MyVoucherStatus Status, int? RemainingUses, int? AccountLimit,
    int ReservedByMe, bool ExpiringSoon, string? CheckoutIssue,
    IReadOnlyList<VoucherAwardResponse> Awards, IReadOnlyList<VoucherUseResponse> Uses);
