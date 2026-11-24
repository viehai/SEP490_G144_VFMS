using VFMS.Contracts.Common;
using VFMS.Contracts.Feedback;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IFeedbackRepository
{
    Task<(IReadOnlyList<TrainerReview> Items, int Total)> SearchAsync(
        string? search, string? status, int? rating, string? memberUserId, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VFMS.Contracts.Feedback.FeedbackTrainerResponse>> GetTrainersAsync(
        CancellationToken cancellationToken = default);
    Task<TrainerReview?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<Package?> GetReviewablePackageAsync(int packageId, CancellationToken cancellationToken);
    Task<PackageReviewsResponse> ListPackageReviewsAsync(int packageId, int page, int? rating, CancellationToken cancellationToken);
    Task<TrainerReviewStatus?> GetPackageReviewStatusAsync(int packageId, string memberId, CancellationToken cancellationToken);
    Task<bool> HasUsedPackageAsync(int packageId, ComponentType type, string memberId, CancellationToken cancellationToken);
    Task<bool> TryAddPackageReviewAsync(TrainerReview review, CancellationToken cancellationToken);
    Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<TrainerReview?> FindExistingAsync(
        string trainerUserId, string memberUserId, CancellationToken cancellationToken = default);
    Task SaveAsync(TrainerReview review, AuditLog? audit = null,
        CancellationToken cancellationToken = default);
}

public interface IFeedbackService
{
    Task<Result<PackageReviewsResponse>> GetPackageReviewsAsync(
        int packageId, string? memberId, int page, int? rating, CancellationToken cancellationToken);
    Task<Result<PagedResponse<FeedbackResponse>>> ListAsync(
        FeedbackListRequest request, string? memberUserId = null,
        CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<FeedbackTrainerResponse>>> GetTrainersAsync(
        CancellationToken cancellationToken = default);
    Task<Result<FeedbackResponse>> SubmitAsync(
        FeedbackSubmitRequest request, string memberUserId,
        CancellationToken cancellationToken = default);
    Task<Result<FeedbackResponse>> ApproveAsync(
        int id, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<Result<FeedbackResponse>> RejectAsync(
        int id, FeedbackReplyRequest request, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<Result<FeedbackResponse>> ReplyAsync(
        int id, FeedbackReplyRequest request, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<Result<FeedbackResponse>> SetVisibilityAsync(
        int id, bool isVisible, string managerUserId, string? ipAddress,
        CancellationToken cancellationToken = default);
}
