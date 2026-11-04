using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Banned words list used to screen Bio, Review, and Announcement content (GBR-66).
/// Managed by Admin (GBR-65).
/// </summary>
public class BannedWordList : BaseEntity
{
    public string Word { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public string CreatedByUserId { get; set; } = string.Empty; // Hoặc AdminId tùy theo tên cột của bạn, đổi từ int sang string
    public ApplicationUser Admin { get; set; } = null!;
}

