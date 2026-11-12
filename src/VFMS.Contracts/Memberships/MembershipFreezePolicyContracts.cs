namespace VFMS.Contracts.Memberships;

public static class MembershipFreezeTierUnits
{
    public const string Days = "Days";
    public const string Months = "Months";
}

public sealed record MembershipFreezePolicyRequest(
    int? MinimumFreezeDays, int? MaximumFreezeDays, int? MinimumRequestDays, int? EligibilityPaidDays,
    IReadOnlyList<MembershipFreezeTierQuotaRequest>? Quotas);

public sealed record MembershipFreezeTierQuotaRequest(int? Duration, string? Unit, int? MaximumCumulativeFreezeDays);

public sealed record MembershipFreezePolicyResponse(
    MembershipFreezePolicyConfiguration? Policy, IReadOnlyList<MembershipFreezeTierQuotaResponse> Quotas);

public sealed record MembershipFreezePolicyConfiguration(
    int PolicyId, int MinimumFreezeDays, int MaximumFreezeDays, int MinimumRequestDays, int EligibilityPaidDays,
    DateTime? UpdatedAt);

public sealed record MembershipFreezeTierQuotaResponse(int Duration, string Unit, int MaximumCumulativeFreezeDays);
