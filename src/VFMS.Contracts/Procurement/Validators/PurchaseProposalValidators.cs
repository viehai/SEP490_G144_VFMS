using FluentValidation;

namespace VFMS.Contracts.Procurement.Validators;

public sealed class PurchaseProposalItemRequestValidator : AbstractValidator<PurchaseProposalItemRequest>
{
    public PurchaseProposalItemRequestValidator()
    {
        RuleFor(x => x.ItemName)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The Item Name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10_000)
            .WithMessage("Quantity must be between 1 and 10000.");
        RuleFor(x => x.EstimatedUnitPrice).InclusiveBetween(1m, 1_000_000_000_000m)
            .WithMessage("Estimated unit price must be at least 1 VND.");
        RuleFor(x => x.EquipmentId).GreaterThan(0).When(x => x.EquipmentId.HasValue);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

/// <summary>GBR-62: description, at least one item, each with name, quantity and estimated unit price.</summary>
public sealed class PurchaseProposalRequestValidator : AbstractValidator<PurchaseProposalRequest>
{
    public PurchaseProposalRequestValidator()
    {
        RuleFor(x => x.Description)
            .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("The Description field is required.")
            .MaximumLength(2000);
        RuleFor(x => x.Items)
            .NotNull().WithMessage("At least one item is required.")
            .Must(items => items is { Count: > 0 }).WithMessage("At least one item is required.")
            .Must(items => items is null || items.Count <= 50).WithMessage("A proposal can contain at most 50 items.");
        RuleForEach(x => x.Items).SetValidator(new PurchaseProposalItemRequestValidator());
    }
}

public sealed class PurchaseProposalListRequestValidator : AbstractValidator<PurchaseProposalListRequest>
{
    private static readonly string[] Statuses = ["Draft", "Submitted", "Approved", "Rejected", "Completed"];

    public PurchaseProposalListRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrWhiteSpace(s) || Statuses.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class PurchaseProposalReviewRequestValidator : AbstractValidator<PurchaseProposalReviewRequest>
{
    public PurchaseProposalReviewRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}
