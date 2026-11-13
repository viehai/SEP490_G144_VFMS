namespace VFMS.Contracts.Memberships;

public static class MembershipFreezeReviewTypes
{
    public const string Freeze = "Freeze";
    public const string EarlyUnfreeze = "EarlyUnfreeze";
}

public sealed class MembershipFreezeReviewListRequest
{
    public string? Type { get; init; }
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
    string? ApprovalMessage, MembershipFreezeDocumentResponse? SupportingDocument = null);

public sealed record RejectMembershipFreezeRequest(string? Reason);
