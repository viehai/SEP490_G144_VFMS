namespace VFMS.Contracts.Memberships;

public sealed record MembershipFreezeRequest(
    int? MembershipId, DateOnly? StartDate, int? RequestedDays, string? Reason, DateOnly? EndDate = null);

public sealed record MembershipFreezeEligibilityResponse(
    MembershipSummaryResponse Membership, bool Eligible, string? MessageCode, string? Message,
    DateOnly Today, DateOnly? EarliestStartDate, int PaidDays, int? RemainingQuota,
    MembershipFreezePolicyConfiguration? Policy, int MaximumAvailableDays = 0, DateOnly? LatestStartDate = null,
    IReadOnlyList<MembershipFreezeBlockedPeriod>? OverlappingPeriods = null, int? GrantedFreezeAllowanceDays = null,
    int CompletedFreezeDays = 0, int CommittedFreezeDays = 0, int? RemainingFreezeCount = null);

public sealed record MembershipFreezeBlockedPeriod(int RequestId, DateOnly StartDate, DateOnly EndDate, string Status);

public sealed record MembershipFreezeRequestResponse(
    int FreezeRequestId, int MembershipId, string PlanName, DateOnly StartDate, DateOnly EndDate,
    int RequestedDays, string? Reason, DateTimeOffset SubmittedAt, string Status,
    string? ReviewNote, DateTimeOffset? ReviewedAt, DateTimeOffset? EarlyUnfreezeRequestedAt,
    string? EarlyUnfreezeStatus, bool CanEdit, bool CanCancel, bool CanRequestEarlyUnfreeze,
    MembershipFreezePolicyResponse? AppliedPolicy,
    string? EarlyUnfreezeReviewNote = null, DateTimeOffset? EarlyUnfreezeReviewedAt = null,
    MembershipFreezeDocumentResponse? SupportingDocument = null,
    DateOnly? ActualStartDate = null, DateOnly? ActualEndDate = null);
