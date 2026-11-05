using VFMS.Contracts.Common;
using VFMS.Contracts.Procurement;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IPurchaseProposalRepository : IAuditedRepository
{
    Task<(IReadOnlyList<PurchaseProposal> Items, int TotalCount)> SearchAsync(
        string? proposedByUserId, PurchaseProposalStatus? status, bool excludeDrafts, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PurchaseProposal?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ProposalNumberExistsAsync(string proposalNumber, CancellationToken cancellationToken = default);
    /// <summary>Returns which of the given equipment IDs exist.</summary>
    Task<IReadOnlySet<int>> GetExistingEquipmentIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}

/// <summary>UC-74 Manage Purchase Proposals (Staff) and UC-75 Approve Purchase Request (Manager). GBR-62, GBR-68.</summary>
public interface IPurchaseProposalService
{
    // Staff (UC-74)
    Task<Result<PagedResponse<PurchaseProposalResponse>>> GetMyProposalsAsync(
        PurchaseProposalListRequest request, string staffUserId, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> CreateAsync(
        PurchaseProposalRequest request, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> UpdateDraftAsync(
        int id, PurchaseProposalRequest request, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> SubmitAsync(
        int id, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>When <paramref name="onlyProposedByUserId"/> is set, other users' proposals are not found.</summary>
    Task<Result<PurchaseProposalResponse>> GetAsync(
        int id, string? onlyProposedByUserId, CancellationToken cancellationToken = default);

    // Manager (UC-75)
    Task<Result<PagedResponse<PurchaseProposalResponse>>> GetForReviewAsync(
        PurchaseProposalListRequest request, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> ApproveAsync(
        int id, string? note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> RejectAsync(
        int id, string? note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> RequestRevisionAsync(
        int id, string? note, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PurchaseProposalResponse>> CompleteAsync(
        int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}
