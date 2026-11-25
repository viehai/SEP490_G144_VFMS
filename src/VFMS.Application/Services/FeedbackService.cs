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

    public async Task<Result<PagedResponse<PublicFeedbackResponse>>> ListPublicAsync(
        FeedbackListRequest request, string? memberUserId, CancellationToken cancellationToken = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100 || request.Rating is < 1 or > 5)
            return Failure<PagedResponse<PublicFeedbackResponse>>("Trang hoặc kích thước trang không hợp lệ.", "INVALID_REVIEW_FILTER", 400);
        var (items, total) = await _repository.SearchPublicAsync(
            request.Search, request.Rating, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<PublicFeedbackResponse>>.Success(new(
            request.Page,
            request.PageSize,
            total,
            items.Select(review => new PublicFeedbackResponse(
                review.Id,
                review.MemberUser.FullName,
                review.Rating,
                review.Comment,
                review.CreatedAt,
                review.Package?.Name,
                review.ManagerReply,
                memberUserId == review.MemberUserId)).ToArray()));
    }

    public async Task<Result<PagedResponse<FeedbackResponse>>> ListForManagementAsync(
        FeedbackListRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100 || request.Rating is < 1 or > 5)
            return Failure<PagedResponse<FeedbackResponse>>("Bộ lọc hoặc kích thước trang không hợp lệ.", "INVALID_REVIEW_FILTER", 400);
        var (items, total) = await _repository.SearchAsync(
            request.Search, request.Status, request.Rating, null, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<FeedbackResponse>>.Success(new(
            request.Page, request.PageSize, total, items.Select(Map).ToArray()));
    }

    public async Task<Result<PagedResponse<FeedbackResponse>>> ListMineAsync(
        FeedbackListRequest request, string memberUserId,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _repository.SearchAsync(
            request.Search, request.Status, request.Rating, memberUserId,
            request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<FeedbackResponse>>.Success(
            new PagedResponse<FeedbackResponse>(
                request.Page, request.PageSize, total, items.Select(Map).ToArray()));
    }

    public async Task<Result<IReadOnlyList<FeedbackTrainerResponse>>> GetTrainersAsync(
        CancellationToken cancellationToken = default) =>
        Result<IReadOnlyList<FeedbackTrainerResponse>>.Success(
            await _repository.GetTrainersAsync(cancellationToken));

    public async Task<Result<PackageReviewsResponse>> GetPackageReviewsAsync(
        int packageId, string? memberId, int page, int? rating, CancellationToken cancellationToken)
    {
        if (page < 1 || rating is < 1 or > 5)
            return Failure<PackageReviewsResponse>("Trang hoặc bộ lọc số sao không hợp lệ.", "INVALID_REVIEW_FILTER", 400);
        var package = await _repository.GetReviewablePackageAsync(packageId, cancellationToken);
        if (package is null) return PackageNotFound<PackageReviewsResponse>();
        var result = await _repository.ListPackageReviewsAsync(packageId, page, rating, cancellationToken);
        if (memberId != null)
        {
            var status = await _repository.GetPackageReviewStatusAsync(packageId, memberId, cancellationToken);
            result = result with
            {
                MyReviewStatus = status?.ToString(),
                CanReview = status == null && await _repository.HasUsedPackageAsync(
                    packageId, package.Components.Single().ComponentType, memberId, cancellationToken)
            };
        }
        return Result<PackageReviewsResponse>.Success(result);
    }

    public async Task<Result<FeedbackResponse>> SubmitAsync(
        FeedbackSubmitRequest request, string memberUserId,
        CancellationToken cancellationToken = default)
    {
        if (request.Rating is < 1 or > 5)
            return Failure<FeedbackResponse>("Điểm đánh giá phải từ 1 đến 5 sao.", "FEEDBACK_RATING_INVALID", 400);
        if (request.Comment?.Length > 1000)
            return Failure<FeedbackResponse>("Nhận xét tối đa 1.000 ký tự.", "FEEDBACK_COMMENT_TOO_LONG", 400);

        Package? package = null;
        if (request.PackageId is int packageId)
        {
            package = await _repository.GetReviewablePackageAsync(packageId, cancellationToken);
            if (package is null) return PackageNotFound<FeedbackResponse>();
            if (await _repository.GetPackageReviewStatusAsync(packageId, memberUserId, cancellationToken) != null)
                return AlreadyReviewed();
            if (!await _repository.HasUsedPackageAsync(packageId, package.Components.Single().ComponentType, memberUserId, cancellationToken))
                return Failure<FeedbackResponse>("Bạn có thể đánh giá sau khi đã tập bằng gói này.", "PACKAGE_USE_REQUIRED", 403);
        }

        var banned = await _bannedWords.FindBannedWordsAsync(request.Comment, cancellationToken);
        if (banned.Count > 0)
            return Failure<FeedbackResponse>(
                "Nội dung chứa từ khóa bị chặn: " + string.Join(", ", banned),
                "CONTENT_BANNED_WORD", 400);

        var member = await _repository.GetUserAsync(memberUserId, cancellationToken);
        if (member is null)
            return Failure<FeedbackResponse>("Không tìm thấy hội viên.", "FEEDBACK_USER_NOT_FOUND", 404);

        // Gym and package feedback publish immediately; PT contract reviews use a separate workflow.
        var review = new TrainerReview
        {
            TrainerUserId = member.UserId,
            MemberUserId = member.UserId,
            TrainerUser = member,
            MemberUser = member,
            Package = package,
            PackageId = package?.Id,
            Rating = request.Rating,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            ReviewStatus = TrainerReviewStatus.Approved,
            IsVisible = true
        };
        if (package is null)
            await _repository.SaveAsync(review, cancellationToken: cancellationToken);
        else if (!await _repository.TryAddPackageReviewAsync(review, cancellationToken))
            return AlreadyReviewed();
        return Result<FeedbackResponse>.Success(Map(review), 201);
    }

    public async Task<Result<FeedbackResponse>> UpdateAsync(
        int id, FeedbackUpdateRequest request, string memberUserId,
        CancellationToken cancellationToken = default)
    {
        if (request.Rating is < 1 or > 5)
            return Failure<FeedbackResponse>("Điểm đánh giá phải từ 1 đến 5 sao.", "FEEDBACK_RATING_INVALID", 400);
        if (request.Comment?.Length > 1000)
            return Failure<FeedbackResponse>("Nhận xét tối đa 1.000 ký tự.", "FEEDBACK_COMMENT_TOO_LONG", 400);
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null || review.MemberUserId != memberUserId) return FeedbackNotFound();
        var banned = await _bannedWords.FindBannedWordsAsync(request.Comment, cancellationToken);
        if (banned.Count > 0)
            return Failure<FeedbackResponse>(
                "Nội dung chứa từ khóa bị chặn: " + string.Join(", ", banned),
                "CONTENT_BANNED_WORD", 400);

        review.Rating = request.Rating;
        review.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        review.ReviewStatus = TrainerReviewStatus.Approved;
        review.IsVisible = true;
        review.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveAsync(review, cancellationToken: cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review));
    }

    public async Task<Result<bool>> DeleteAsync(int id, string memberUserId, CancellationToken cancellationToken = default)
    {
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null || review.MemberUserId != memberUserId)
            return Result<bool>.Failure("Không tìm thấy đánh giá.", "FEEDBACK_NOT_FOUND", 404);
        await _repository.DeleteAsync(review, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<FeedbackResponse>> ReplyAsync(
        int id, FeedbackReplyRequest request, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reply))
            return Failure<FeedbackResponse>("Nội dung trả lời không được để trống.", "FEEDBACK_REPLY_REQUIRED", 400);
        if (request.Reply.Length > 1000)
            return Failure<FeedbackResponse>("Nội dung trả lời tối đa 1.000 ký tự.", "FEEDBACK_REPLY_TOO_LONG", 400);
        var banned = await _bannedWords.FindBannedWordsAsync(request.Reply, cancellationToken);
        if (banned.Count > 0)
            return Failure<FeedbackResponse>("Nội dung trả lời chứa từ khóa bị chặn: " + string.Join(", ", banned),
                "CONTENT_BANNED_WORD", 400);
        var review = await _repository.GetAsync(id, cancellationToken);
        if (review is null) return FeedbackNotFound();

        var previousReply = review.ManagerReply;
        var newReply = request.Reply.Trim();
        review.ManagerReply = newReply;
        review.ReviewedByUserId = managerUserId;
        review.ReviewedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveAsync(review, Audit(managerUserId, ipAddress,
            previousReply, newReply), cancellationToken);
        return Result<FeedbackResponse>.Success(Map(review));
    }

    private static AuditLog Audit(
        string userId, string? ipAddress, string? oldReply, string newReply) => new()
    {
        ActorUserId = userId,
        ActionType = AuditActionType.Update,
        AuditEntity = nameof(TrainerReview),
        OldValue = oldReply,
        NewValue = newReply,
        OccurredAt = DateTime.UtcNow,
        IpAddress = ipAddress
    };

    private static FeedbackResponse Map(TrainerReview review) => new(
        review.Id, review.TrainerUserId, review.TrainerUser?.FullName,
        review.MemberUserId, review.MemberUser?.FullName ?? "Hội viên",
        review.Rating, review.Comment, review.ReviewStatus.ToString(),
        review.ManagerReply, review.IsVisible, review.CreatedAt, review.ReviewedAt,
        review.ReviewedByUser?.FullName, review.Package?.Name);

    private static Result<T> Failure<T>(string message, string code, int status) =>
        Result<T>.Failure(message, code, status);
    private static Result<FeedbackResponse> FeedbackNotFound() =>
        Result<FeedbackResponse>.Failure("Không tìm thấy đánh giá.", "FEEDBACK_NOT_FOUND", 404);
    private static Result<T> PackageNotFound<T>() =>
        Failure<T>("Không tìm thấy gói tập đang mở bán.", "PACKAGE_NOT_FOUND", 404);
    private static Result<FeedbackResponse> AlreadyReviewed() =>
        Failure<FeedbackResponse>("Bạn đã gửi đánh giá cho gói này.", "PACKAGE_ALREADY_REVIEWED", 409);
}
