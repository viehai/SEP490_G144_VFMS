using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

public class ConsultationMessage : BaseEntity
{
    public int ConversationId { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;

    public ConsultationConversation Conversation { get; set; } = null!;
    public ApplicationUser SenderUser { get; set; } = null!;
}
