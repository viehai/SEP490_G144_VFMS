using VFMS.Contracts.Common;
using VFMS.Contracts.Feedback;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IFeedbackRepository
{
    Task<(IReadOnlyList<TrainerReview> Items, int Total)> SearchAsync(
        string? search, string? status, int? rating, string? memberUserId, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VFMS.Contracts.Feedback.FeedbackTrainerResponse>> GetTrainersAsync(
        CancellationToken cancellationToken = default);
    Task<TrainerReview?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<TrainerReview?> FindExistingAsync(
        string trainerUserId, string memberUserId, CancellationToken cancellationToken = default);
    Task SaveAsync(TrainerReview review, AuditLog? audit = null,
        CancellationToken cancellationToken = default);
}

public interface IFeedbackService
{
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
