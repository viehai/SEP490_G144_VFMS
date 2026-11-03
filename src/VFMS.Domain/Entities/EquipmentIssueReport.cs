using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Equipment issue report submitted by a Member (UC-71).
/// Staff can Resolve, Escalate, or Mark as Safe (UC-72).
/// </summary>
public class EquipmentIssueReport : BaseEntity
{
    public int EquipmentId { get; set; }
    public string ReportedByUserId { get; set; } = string.Empty;
    public string? HandledByUserId { get; set; }
    public IncidentType IncidentType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public EquipmentIssueStatus IssueStatus { get; set; } = EquipmentIssueStatus.Reported;
    public DateTime ReportedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Equipment Equipment { get; set; } = null!;
    public ApplicationUser ReportedByUser { get; set; } = null!;
    public ApplicationUser? HandledByUser { get; set; }
    public ICollection<EquipmentSafetyConfirmation> SafetyConfirmations { get; set; } = [];
}

