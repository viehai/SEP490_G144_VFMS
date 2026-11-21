using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

public class ConsultationConversation : BaseEntity
{
    public string CustomerUserId { get; set; } = string.Empty;
    public string? ManagerUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser CustomerUser { get; set; } = null!;
    public ApplicationUser? ManagerUser { get; set; }
    public ICollection<ConsultationMessage> Messages { get; set; } = [];
}
