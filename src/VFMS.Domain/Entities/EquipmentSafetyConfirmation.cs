using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Confirmation that a reported equipment issue has been resolved and verified safe for use.
/// </summary>
public class EquipmentSafetyConfirmation : BaseEntity
{
    public int EquipmentId { get; set; }
    public int EquipmentIssueReportId { get; set; }
    public string ConfirmedByUserId { get; set; } = string.Empty;
    public DateTime ConfirmedAt { get; set; }
    public string? Note { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Equipment Equipment { get; set; } = null!;
    public EquipmentIssueReport EquipmentIssueReport { get; set; } = null!;
    public ApplicationUser ConfirmedByUser { get; set; } = null!;
}

