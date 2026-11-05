using Mapster;
using VFMS.Contracts.Procurement;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Mappings;

public sealed class ProcurementMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PurchaseProposalItem, PurchaseProposalItemResponse>()
            .Map(d => d.EquipmentName, s => s.Equipment == null ? null : s.Equipment.EquipmentName)
            .Map(d => d.LineTotal, s => s.Quantity * s.EstimatedUnitPrice);

        config.NewConfig<PurchaseProposalReview, PurchaseProposalReviewResponse>()
            .Map(d => d.Action, s => s.Action.ToString())
            .Map(d => d.ReviewedByName, s => s.ReviewedByUser == null ? null : s.ReviewedByUser.FullName);

        config.NewConfig<PurchaseProposal, PurchaseProposalResponse>()
            .Map(d => d.Status, s => s.ProposalStatus.ToString())
            .Map(d => d.SubmittedAt, s => s.ProposalStatus == PurchaseProposalStatus.Draft ? (DateTime?)null : s.SubmittedAt)
            .Map(d => d.ProposedByName, s => s.ProposedByUser == null ? null : s.ProposedByUser.FullName)
            // Items replaced during a draft edit are soft-deleted but can still be in the tracked collection.
            .Map(d => d.TotalBudget, s => s.Items.Where(i => !i.IsDeleted).Sum(i => i.Quantity * i.EstimatedUnitPrice))
            .Map(d => d.Items, s => s.Items.Where(i => !i.IsDeleted).OrderBy(i => i.Id))
            .Map(d => d.Reviews, s => s.Reviews.OrderBy(r => r.ReviewedAt).ThenBy(r => r.Id));
    }
}
