namespace VFMS.Contracts.Memberships;

public static class MembershipFreezeTierUnits
{
    public const string Days = "Days";
    public const string Months = "Months";
}

public sealed record MembershipFreezePolicyRequest(
    int? MinimumFreezeDays, int? MaximumFreezeDays, int? MinimumRequestDays, int? EligibilityPaidDays,
    IReadOnlyList<MembershipFreezeTierQuotaRequest>? Quotas = null,
    MembershipDurationUnit MinimumFreezeDaysUnit = MembershipDurationUnit.Day,
    MembershipDurationUnit MaximumFreezeDaysUnit = MembershipDurationUnit.Day,
    MembershipDurationUnit MinimumRequestDaysUnit = MembershipDurationUnit.Day,
    MembershipDurationUnit EligibilityPaidDaysUnit = MembershipDurationUnit.Day,
    int? DefaultMaxFreezeCount = null);

public sealed record MembershipFreezeTierQuotaRequest(int? Duration, string? Unit, int? MaximumCumulativeFreezeDays);

public sealed record MembershipFreezePolicyResponse(
    MembershipFreezePolicyConfiguration? Policy, IReadOnlyList<MembershipFreezeTierQuotaResponse>? Quotas = null,
    int? GrantedFreezeAllowanceDays = null, int? GrantedFreezeCount = null);

public sealed record MembershipFreezePolicyConfiguration(
    int PolicyId, int MinimumFreezeDays, int MaximumFreezeDays, int MinimumRequestDays, int EligibilityPaidDays,
    DateTime? UpdatedAt,
    MembershipDurationUnit MinimumFreezeDaysUnit = MembershipDurationUnit.Day,
    MembershipDurationUnit MaximumFreezeDaysUnit = MembershipDurationUnit.Day,
    MembershipDurationUnit MinimumRequestDaysUnit = MembershipDurationUnit.Day,
    MembershipDurationUnit EligibilityPaidDaysUnit = MembershipDurationUnit.Day,
    int? DefaultMaxFreezeCount = null);

public sealed record MembershipFreezeTierQuotaResponse(int Duration, string Unit, int MaximumCumulativeFreezeDays);
