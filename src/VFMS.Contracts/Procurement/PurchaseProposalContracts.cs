namespace VFMS.Contracts.Procurement;

/// <summary>What to buy and how many. Staff do not enter a price; the Manager records it when the purchase is done.</summary>
public sealed record PurchaseProposalItemRequest(
    string ItemName,
    int Quantity,
    int? EquipmentId = null,
    string? Reason = null
);

/// <summary>
/// Create or edit a proposal (UC-74). <see cref="Submit"/> = true sends it to the Manager ("Submitted");
/// false saves it as a "Draft".
/// </summary>
public sealed record PurchaseProposalRequest(
    string Description,
    IReadOnlyList<PurchaseProposalItemRequest> Items,
    bool Submit = true
);

/// <summary>Status: Draft, Submitted, Approved, Rejected, Completed.</summary>
public sealed record PurchaseProposalListRequest(
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);

/// <summary>Review note. Required to reject or request a revision, optional to approve.</summary>
public sealed record PurchaseProposalReviewRequest(string? Note = null);

/// <summary>The price actually paid per unit for one item of the proposal.</summary>
public sealed record PurchaseProposalItemPriceRequest(int ItemId, decimal UnitPrice);

/// <summary>Marks an approved proposal as purchased, with the real unit price of every item.</summary>
public sealed record PurchaseProposalCompleteRequest(IReadOnlyList<PurchaseProposalItemPriceRequest> Items);

public sealed record PurchaseProposalItemResponse(
    int Id,
    string ItemName,
    int? EquipmentId,
    string? EquipmentName,
    int Quantity,
    decimal? UnitPrice,
    decimal? LineTotal,
    string? Reason
);

public sealed record PurchaseProposalReviewResponse(
    int Id,
    string Action,
    string? ReviewNote,
    string ReviewedByUserId,
    string? ReviewedByName,
    DateTime ReviewedAt
);

public sealed record PurchaseProposalResponse(
    int Id,
    string ProposalNumber,
    string? Description,
    string Status,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    string ProposedByUserId,
    string? ProposedByName,
    /// <summary>What the purchase cost, once the Manager has entered the real prices; null before that.</summary>
    decimal? TotalCost,
    IReadOnlyCollection<PurchaseProposalItemResponse> Items,
    IReadOnlyCollection<PurchaseProposalReviewResponse> Reviews
);
