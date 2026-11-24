using VFMS.Contracts.Common;

namespace VFMS.Contracts.Feedback;

public sealed record FeedbackListRequest(
    string? Search = null,
    string? Status = null,
    int? Rating = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record FeedbackReplyRequest(string? Reply);

public sealed record FeedbackSubmitRequest(string? TrainerUserId, int Rating, string? Comment, int? PackageId = null);

public sealed record FeedbackResponse(
    int Id,
    string? TrainerUserId,
    string? TrainerName,
    string MemberUserId,
    string MemberName,
    int Rating,
    string? Comment,
    string Status,
    string? ManagerReply,
    bool IsVisible,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    string? ReviewedByName,
    string? PackageName = null
);

public sealed record PublicPackageReview(
    int Id, string MemberName, int Rating, string? Comment, DateTime CreatedAt, string? ManagerReply);

public sealed record PackageReviewsResponse(
    int TotalReviews, double? AverageRating, PagedResponse<PublicPackageReview> Reviews)
{
    public const int PageSize = 5;
    public bool CanReview { get; init; }
    public string? MyReviewStatus { get; init; }
}

public sealed record FeedbackTrainerResponse(
    string UserId,
    string Name,
    string? AvatarUrl,
    string? Bio,
    string Level
);
