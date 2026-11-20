namespace VFMS.Contracts.Consultations;

public sealed record ConsultationCreateRequest(string Subject, string Message);
public sealed record ConsultationSendRequest(string Message);

public sealed record ConsultationMessageResponse(
    int Id,
    string SenderUserId,
    string SenderName,
    string Content,
    bool IsRead,
    DateTime CreatedAt
);

public sealed record ConsultationConversationResponse(
    int Id,
    string CustomerUserId,
    string CustomerName,
    string? ManagerUserId,
    string? ManagerName,
    string Subject,
    string Status,
    DateTime LastMessageAt,
    IReadOnlyCollection<ConsultationMessageResponse> Messages
);

public sealed record ConsultationListRequest(
    string? Search = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20
);
