namespace VFMS.Contracts.Memberships;

public static class MembershipFreezeReviewTypes
{
    public const string Freeze = "Freeze";
    public const string EarlyUnfreeze = "EarlyUnfreeze";
}

public sealed class MembershipFreezeReviewListRequest
{
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record MembershipFreezeReviewSummaryResponse(
    int FreezeRequestId, string RequestType, string MemberName, string MemberEmail, string PlanName,
    DateOnly StartDate, DateOnly EndDate, int RequestedDays, DateTimeOffset SubmittedAt, string Status);

public sealed record MembershipFreezeReviewDetailResponse(
    MembershipFreezeReviewSummaryResponse Request, MembershipSummaryResponse Membership,
    string? Reason, string? ReviewNote, DateTimeOffset? ReviewedAt, string? EarlyUnfreezeReviewNote,
    DateTimeOffset? EarlyUnfreezeReviewedAt, int? RemainingQuota, MembershipFreezePolicyResponse? AppliedPolicy,
    int? AppliedExpiryExtensionDays, DateOnly Today, int? ActualFrozenDays, bool CanApprove, bool CanReject,
    string? ApprovalMessage, MembershipFreezeDocumentResponse? SupportingDocument = null,
    int? GrantedFreezeAllowanceDays = null, int CommittedFreezeDays = 0, int? GrantedFreezeCount = null, int UsedFreezeCount = 0);

public sealed record RejectMembershipFreezeRequest(string? Reason);
