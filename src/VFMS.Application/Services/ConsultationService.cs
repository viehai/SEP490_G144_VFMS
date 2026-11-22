using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Consultations;
using VFMS.Domain.Entities;

namespace VFMS.Application.Services;

public sealed class ConsultationService : IConsultationService
{
    private readonly IConsultationRepository _repository;
    private readonly IBannedWordService _bannedWords;

    public ConsultationService(IConsultationRepository repository, IBannedWordService bannedWords)
    {
        _repository = repository;
        _bannedWords = bannedWords;
    }

    public async Task<Result<PagedResponse<ConsultationConversationResponse>>> ListAsync(
        ConsultationListRequest request, string? customerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _repository.SearchAsync(
            request.Search, request.Status, customerUserId, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<ConsultationConversationResponse>>.Success(
            new PagedResponse<ConsultationConversationResponse>(
                request.Page, request.PageSize, total, items.Select(Map).ToArray()));
    }

    public async Task<Result<ConsultationConversationResponse>> GetAsync(
        int id, string userId, bool isManager, CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetAsync(id, cancellationToken);
        if (conversation is null)
            return NotFound();
        if (!isManager && conversation.CustomerUserId != userId)
            return Forbidden();
        return Result<ConsultationConversationResponse>.Success(Map(conversation));
    }

    public async Task<Result<ConsultationConversationResponse>> CreateAsync(
        ConsultationCreateRequest request, string customerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
            return Result<ConsultationConversationResponse>.Failure(
                "Tiêu đề và nội dung tư vấn không được để trống.", "CONSULTATION_REQUIRED", 400);
        var forbidden = await FindBannedAsync(request.Subject + " " + request.Message, cancellationToken);
        if (forbidden is not null) return forbidden;

        var customer = await _repository.GetUserAsync(customerUserId, cancellationToken);
        if (customer is null)
            return Forbidden();

        var conversation = new ConsultationConversation
        {
            CustomerUserId = customerUserId,
            CustomerUser = customer,
            Subject = request.Subject.Trim(),
            Status = "Open",
            LastMessageAt = DateTime.UtcNow
        };
        var message = new ConsultationMessage
        {
            SenderUserId = customerUserId,
            SenderUser = customer,
            Content = request.Message.Trim(),
            IsRead = false
        };
        var saved = await _repository.CreateAsync(conversation, message, cancellationToken);
        return Result<ConsultationConversationResponse>.Success(Map(saved), 201);
    }

    public async Task<Result<ConsultationConversationResponse>> SendAsync(
        int id, ConsultationSendRequest request, string senderUserId, bool isManager,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Result<ConsultationConversationResponse>.Failure(
                "Nội dung tin nhắn không được để trống.", "CONSULTATION_MESSAGE_REQUIRED", 400);
        var forbidden = await FindBannedAsync(request.Message, cancellationToken);
        if (forbidden is not null) return forbidden;

        var conversation = await _repository.GetAsync(id, cancellationToken);
        if (conversation is null) return NotFound();
        if (!isManager && conversation.CustomerUserId != senderUserId) return Forbidden();
        if (conversation.Status == "Closed")
            return Result<ConsultationConversationResponse>.Failure(
                "Cuộc tư vấn đã đóng.", "CONSULTATION_CLOSED", 409);

        var sender = await _repository.GetUserAsync(senderUserId, cancellationToken);
        if (sender is null) return Forbidden();
        var message = new ConsultationMessage
        {
            ConversationId = id,
            Conversation = conversation,
            SenderUserId = senderUserId,
            SenderUser = sender,
            Content = request.Message.Trim()
        };
        await _repository.AddMessageAsync(
            conversation, message, isManager ? senderUserId : null, cancellationToken);
        return Result<ConsultationConversationResponse>.Success(Map(conversation));
    }

    public async Task<Result<ConsultationConversationResponse>> CloseAsync(
        int id, string managerUserId, CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetAsync(id, cancellationToken);
        if (conversation is null) return NotFound();
        conversation.Status = "Closed";
        conversation.UpdatedAt = DateTime.UtcNow;
        await _repository.AddMessageAsync(conversation, new ConsultationMessage
        {
            ConversationId = id,
            Conversation = conversation,
            SenderUserId = managerUserId,
            Content = "Cuộc tư vấn đã được đóng.",
            SenderUser = await _repository.GetUserAsync(managerUserId, cancellationToken) ?? throw new InvalidOperationException()
        }, managerUserId, cancellationToken);
        return Result<ConsultationConversationResponse>.Success(Map(conversation));
    }

    private async Task<Result<ConsultationConversationResponse>?> FindBannedAsync(
        string text, CancellationToken cancellationToken)
    {
        var banned = await _bannedWords.FindBannedWordsAsync(text, cancellationToken);
        return banned.Count > 0
            ? Result<ConsultationConversationResponse>.Failure(
                "Nội dung chứa từ khóa bị chặn: " + string.Join(", ", banned),
                "CONTENT_BANNED_WORD", 400)
            : null;
    }

    private static ConsultationConversationResponse Map(ConsultationConversation conversation) =>
        new(conversation.Id,
            conversation.CustomerUserId,
            string.IsNullOrWhiteSpace(conversation.CustomerUser?.FullName)
                ? "Khách hàng"
                : conversation.CustomerUser.FullName,
            conversation.ManagerUserId,
            conversation.ManagerUser?.FullName,
            conversation.Subject,
            conversation.Status,
            conversation.LastMessageAt,
            conversation.Messages
                .OrderBy(message => message.CreatedAt)
                .Select(message => new ConsultationMessageResponse(
                    message.Id,
                    message.SenderUserId,
                    message.SenderUser?.FullName ?? "Người dùng",
                    message.Content,
                    message.IsRead,
                    message.CreatedAt))
                .ToArray());

    private static Result<ConsultationConversationResponse> NotFound() =>
        Result<ConsultationConversationResponse>.Failure("Không tìm thấy cuộc tư vấn.", "CONSULTATION_NOT_FOUND", 404);
    private static Result<ConsultationConversationResponse> Forbidden() =>
        Result<ConsultationConversationResponse>.Failure("Bạn không có quyền truy cập cuộc tư vấn này.", "CONSULTATION_FORBIDDEN", 403);
}
