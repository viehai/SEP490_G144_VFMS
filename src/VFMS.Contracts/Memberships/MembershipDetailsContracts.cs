namespace VFMS.Contracts.Memberships;

public sealed record MembershipSummaryResponse(
    int MembershipId, int MembershipPlanId, string PlanName, string ComponentType,
    DateOnly StartDate, DateOnly EndDate, string Status);

public sealed record MembershipDetailResponse(
    MembershipSummaryResponse Membership, DateTimeOffset? PurchasedAt, string? InvoiceNumber,
    MembershipFreezeSummaryResponse Freeze);

public sealed record MembershipFreezeSummaryResponse(
    int TotalFrozenDays, int FreezeCount, IReadOnlyList<MembershipFreezePeriodResponse> History);

public sealed record MembershipFreezePeriodResponse(
    DateOnly RequestedStartDate, DateOnly RequestedEndDate, DateOnly? ActualStartDate,
    DateOnly? ActualEndDate, string Status);
