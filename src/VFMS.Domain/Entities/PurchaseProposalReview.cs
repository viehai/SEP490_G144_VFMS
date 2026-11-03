using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Manager's approval or rejection decision on a <see cref="PurchaseProposal"/> (UC-75).
/// </summary>
public class PurchaseProposalReview : BaseEntity
{
    public int PurchaseProposalId { get; set; }
    public string ReviewedByUserId { get; set; } = string.Empty;
    public PurchaseProposalReviewAction Action { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime ReviewedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PurchaseProposal PurchaseProposal { get; set; } = null!;
    public ApplicationUser ReviewedByUser { get; set; } = null!;
}

