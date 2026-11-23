namespace VFMS.Contracts.Memberships;

public static class MembershipStatuses
{
    public const string AwaitingActivation = "AwaitingActivation";
    public const string Scheduled = "Scheduled";
    public const string Active = "Active";
    public const string Frozen = "Frozen";
    public const string Expired = "Expired";
    public const string Suspended = "Suspended";
    public const string Cancelled = "Cancelled";
}

public sealed record MembershipSummaryResponse(
    int MembershipId, int MembershipPlanId, string PlanName, string ComponentType,
    DateOnly? StartDate, DateOnly? EndDate, string Status, MembershipActivationWindow? Activation = null);

public sealed record MembershipActivationWindow(DateOnly PurchasedOn, DateOnly EarliestDate, DateOnly Deadline);

public sealed record ScheduleMembershipActivationRequest(DateOnly? StartDate);

public sealed record MembershipDetailResponse(
    MembershipSummaryResponse Membership, DateTimeOffset? PurchasedAt, string? InvoiceNumber,
    MembershipFreezeSummaryResponse Freeze);

public sealed record MembershipFreezeSummaryResponse(
    int TotalFrozenDays, int FreezeCount, IReadOnlyList<MembershipFreezePeriodResponse> History);

public sealed record MembershipFreezePeriodResponse(
    DateOnly RequestedStartDate, DateOnly RequestedEndDate, DateOnly? ActualStartDate,
    DateOnly? ActualEndDate, string Status);
