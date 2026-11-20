using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Feedback;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _repository;
    private readonly IBannedWordService _bannedWords;

    public FeedbackService(IFeedbackRepository repository, IBannedWordService bannedWords)
    {
        _repository = repository;
        _bannedWords = bannedWords;
    }

    public async Task<Result<PagedResponse<FeedbackResponse>>> ListAsync(
        FeedbackListRequest request, string? memberUserId = null,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _repository.SearchAsync(
            request.Search, request.Status, request.Rating,
            memberUserId, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<FeedbackResponse>>.Success(
            new PagedResponse<FeedbackResponse>(
                request.Page, request.PageSize, total, items.Select(Map).ToArray()));
    }

    public async Task<Result<IReadOnlyList<FeedbackTrainerResponse>>> GetTrainersAsync(
        CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<FeedbackTrainerResponse>>.Success(
            await _repository.GetTrainersAsync(cancellationToken));

    public async Task<Result<FeedbackResponse>> SubmitAsync(
        FeedbackSubmitRequest request, string memberUserId,
        CancellationToken cancellationToken = default)
    {
        if (request.Rating is < 1 or > 5)
            return Failure<FeedbackResponse>("Điểm đánh giá phải từ 1 đến 5 sao.", "FEEDBACK_RATING_INVALID", 400);

        var banned = await _bannedWords.FindBannedWordsAsync(request.Comment, cancellationToken);
        if (banned.Count > 0)
            return Failure<FeedbackResponse>(
                "Nội dung chứa từ khóa bị chặn: " + string.Join(", ", banned),
                "CONTENT_BANNED_WORD", 400);

        var member = await _repository.GetUserAsync(memberUserId, cancellationToken);
        if (member is null)
            return Failure<FeedbackResponse>("Không tìm thấy hội viên.", "FEEDBACK_USER_NOT_FOUND", 404);

        ApplicationUser? trainer = null;
        if (!string.IsNullOrWhiteSpace(request.TrainerUserId))
        {
            var trainers = await _repository.GetTrainersAsync(cancellationToken);
            if (trainers.Count == 0)
                return Failure<FeedbackResponse>("Hệ thống chưa có huấn luyện viên để lưu đánh giá.", "FEEDBACK_TRAINER_NOT_FOUND", 404);
            trainer = await _repository.GetUserAsync(trainers[0].UserId, cancellationToken);
            if (trainer is null)
                return Failure<FeedbackResponse>("Hệ thống chưa có huấn luyện viên để lưu đánh giá.", "FEEDBACK_TRAINER_NOT_FOUND", 404);
        }

        var review = new TrainerReview
        {
            TrainerUserId = trainer?.UserId,
            MemberUserId = member.UserId,
            TrainerUser = trainer,
            MemberUser = member,
            Rating = request.Rating,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            ReviewStatus = TrainerReviewStatus.Pending,
            IsVisible = false
        };
        await _repository.SaveAsync(review, cancellationToken: cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review), 201);
    }

    public async Task<Result<FeedbackResponse>> ApproveAsync(
        int id, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null) return FeedbackNotFound();
        review.ReviewStatus = TrainerReviewStatus.Approved;
        review.IsVisible = true;
        review.ReviewedByUserId = managerUserId;
        review.ReviewedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveAsync(review, Audit(managerUserId, ipAddress, nameof(TrainerReview),
            "Status=Pending", "Status=Approved"), cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review));
    }

    public async Task<Result<FeedbackResponse>> RejectAsync(
        int id, FeedbackReplyRequest request, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reply))
            return Failure<FeedbackResponse>("Vui lòng nhập lý do từ chối.", "FEEDBACK_REPLY_REQUIRED", 400);
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null) return FeedbackNotFound();
        review.ReviewStatus = TrainerReviewStatus.Rejected;
        review.IsVisible = false;
        review.ManagerReply = request.Reply.Trim();
        review.ReviewedByUserId = managerUserId;
        review.ReviewedAt = DateTime.UtcNow;
        await _repository.SaveAsync(review, Audit(managerUserId, ipAddress, nameof(TrainerReview),
            "Status=Pending", "Status=Rejected"), cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review));
    }

    public async Task<Result<FeedbackResponse>> ReplyAsync(
        int id, FeedbackReplyRequest request, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reply))
            return Failure<FeedbackResponse>("Nội dung phản hồi không được để trống.", "FEEDBACK_REPLY_REQUIRED", 400);
        var banned = await _bannedWords.FindBannedWordsAsync(request.Reply, cancellationToken);
        if (banned.Count > 0)
            return Failure<FeedbackResponse>("Phản hồi chứa từ khóa bị chặn: " + string.Join(", ", banned),
                "CONTENT_BANNED_WORD", 400);
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null) return FeedbackNotFound();
        review.ManagerReply = request.Reply.Trim();
        review.ReviewedByUserId = managerUserId;
        review.ReviewedAt = DateTime.UtcNow;
        await _repository.SaveAsync(review, Audit(managerUserId, ipAddress, nameof(TrainerReview),
            null, "ManagerReply updated"), cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review));
    }

    public async Task<Result<FeedbackResponse>> SetVisibilityAsync(
        int id, bool isVisible, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null) return FeedbackNotFound();
        review.IsVisible = isVisible;
        review.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveAsync(review, Audit(managerUserId, ipAddress, nameof(TrainerReview),
            $"IsVisible={!isVisible}", $"IsVisible={isVisible}"), cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review));
    }

    private static AuditLog Audit(string userId, string? ipAddress, string entity,
        string? oldValue, string? newValue) => new()
    {
        ActorUserId = userId,
        ActionType = AuditActionType.Update,
        AuditEntity = entity,
        OldValue = oldValue,
        NewValue = newValue,
        OccurredAt = DateTime.UtcNow,
        IpAddress = ipAddress
    };

    private static FeedbackResponse Map(TrainerReview review) => new(
        review.Id, review.TrainerUserId, review.TrainerUser?.FullName,
        review.MemberUserId, review.MemberUser?.FullName ?? "Hội viên",
        review.Rating, review.Comment, review.ReviewStatus.ToString(),
        review.ManagerReply, review.IsVisible, review.CreatedAt, review.ReviewedAt,
        review.ReviewedByUser?.FullName);

    private static Result<T> Failure<T>(string message, string code, int status) =>
        Result<T>.Failure(message, code, status);
    private static Result<FeedbackResponse> FeedbackNotFound() =>
        Result<FeedbackResponse>.Failure("Không tìm thấy phản hồi.", "FEEDBACK_NOT_FOUND", 404);
}
