namespace VFMS.Contracts.Feedback;

public sealed record FeedbackListRequest(
    string? Search = null,
    string? Status = null,
    int? Rating = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record FeedbackReplyRequest(string? Reply);

public sealed record FeedbackSubmitRequest(string? TrainerUserId, int Rating, string? Comment);

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
    string? ReviewedByName
);

public sealed record FeedbackTrainerResponse(
    string UserId,
    string Name,
    string? AvatarUrl,
    string? Bio,
    string Level
);
