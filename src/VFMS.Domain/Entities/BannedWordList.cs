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
    public string? AddedByUserId { get; set; }
}

