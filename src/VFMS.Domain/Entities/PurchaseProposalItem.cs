using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// A single line item within a <see cref="PurchaseProposal"/>.
/// </summary>
public class PurchaseProposalItem : BaseEntity
{
    public int PurchaseProposalId { get; set; }
    public int? EquipmentId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal EstimatedUnitPrice { get; set; }
    public string? Reason { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public PurchaseProposal PurchaseProposal { get; set; } = null!;
    public Equipment? Equipment { get; set; }
}

