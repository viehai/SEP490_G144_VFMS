using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Multi-item purchase proposal submitted by Staff for Manager approval (UC-74, UC-75).
/// </summary>
public class PurchaseProposal : BaseEntity
{
    public string ProposedByUserId { get; set; } = string.Empty;
    public string ProposalNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PurchaseProposalStatus ProposalStatus { get; set; } = PurchaseProposalStatus.Draft;
    public DateTime SubmittedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser ProposedByUser { get; set; } = null!;
    public ICollection<PurchaseProposalItem> Items { get; set; } = [];
    public ICollection<PurchaseProposalReview> Reviews { get; set; } = [];
}

