using VFMS.Contracts.Common;
using VFMS.Contracts.Consultations;

namespace VFMS.Application.Interfaces;

public interface IConsultationRepository
{
    Task<(IReadOnlyList<VFMS.Domain.Entities.ConsultationConversation> Items, int Total)> SearchAsync(
        string? search, string? status, string? customerUserId, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<VFMS.Domain.Entities.ConsultationConversation?> GetAsync(
        int id, CancellationToken cancellationToken = default);
    Task<VFMS.Domain.Entities.ApplicationUser?> GetUserAsync(
        string userId, CancellationToken cancellationToken = default);
    Task<VFMS.Domain.Entities.ConsultationConversation> CreateAsync(
        VFMS.Domain.Entities.ConsultationConversation conversation,
        VFMS.Domain.Entities.ConsultationMessage message,
        CancellationToken cancellationToken = default);
    Task<VFMS.Domain.Entities.ConsultationMessage> AddMessageAsync(
        VFMS.Domain.Entities.ConsultationConversation conversation,
        VFMS.Domain.Entities.ConsultationMessage message,
        string? managerUserId,
        CancellationToken cancellationToken = default);
}

public interface IConsultationService
{
    Task<Result<PagedResponse<ConsultationConversationResponse>>> ListAsync(
        ConsultationListRequest request, string? customerUserId = null,
        CancellationToken cancellationToken = default);
    Task<Result<ConsultationConversationResponse>> GetAsync(
        int id, string userId, bool isManager, CancellationToken cancellationToken = default);
    Task<Result<ConsultationConversationResponse>> CreateAsync(
        ConsultationCreateRequest request, string customerUserId,
        CancellationToken cancellationToken = default);
    Task<Result<ConsultationConversationResponse>> SendAsync(
        int id, ConsultationSendRequest request, string senderUserId, bool isManager,
        CancellationToken cancellationToken = default);
    Task<Result<ConsultationConversationResponse>> CloseAsync(
        int id, string managerUserId, CancellationToken cancellationToken = default);
}
