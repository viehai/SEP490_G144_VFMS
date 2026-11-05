using FluentValidation;

namespace VFMS.Contracts.Supplements.Validators;

internal static class SupplementRules
{
    public static readonly string[] ProductStatuses = ["Active", "Inactive", "OutOfStock"];
    public static readonly string[] OrderStatuses =
        ["Pending", "Processing", "ReadyForPickup", "OutForDelivery", "Completed", "Cancelled"];
    public static readonly string[] FulfillmentTypes = ["GymPickup", "HomeDelivery"];
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    public static bool IsOneOf(string? value, string[] allowed) =>
        value is not null && allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsBlankOrOneOf(string? value, string[] allowed) =>
        string.IsNullOrWhiteSpace(value) || IsOneOf(value, allowed);

    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>An absolute http(s) link whose path ends in .png, .jpg or .jpeg.</summary>
    public static bool IsImageUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp) &&
        ImageExtensions.Any(ext => parsed.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}

/// <summary>UC-83 product form.</summary>
public sealed class SupplementProductRequestValidator : AbstractValidator<SupplementProductRequest>
{
    public SupplementProductRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(SupplementRules.HasText).WithMessage("The Name field is required.")
            .MaximumLength(256);
        RuleFor(x => x.Description)
            .Must(SupplementRules.HasText).WithMessage("The Description field is required.")
            .MaximumLength(4000);
        RuleFor(x => x.ImageUrl)
            .Must(SupplementRules.HasText).WithMessage("The Image field is required.")
            .MaximumLength(1000);
        // Separate rule: a When() inside the chain above would also switch off the "required" check.
        RuleFor(x => x.ImageUrl)
            .Must(SupplementRules.IsImageUrl).When(x => SupplementRules.HasText(x.ImageUrl))
            .WithMessage("The image must be an http(s) link to a png, jpg or jpeg file.");
        RuleFor(x => x.UnitPrice).InclusiveBetween(1m, 1_000_000_000m)
            .WithMessage("Unit price must be at least 1 VND.");
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(s => SupplementRules.IsOneOf(s, SupplementRules.ProductStatuses))
            .WithMessage("Status must be Active, Inactive or OutOfStock.");
    }
}

public sealed class SupplementProductStatusRequestValidator : AbstractValidator<SupplementProductStatusRequest>
{
    public SupplementProductStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => SupplementRules.IsOneOf(s, SupplementRules.ProductStatuses))
            .WithMessage("Status must be Active, Inactive or OutOfStock.");
    }
}

public sealed class SupplementProductListRequestValidator : AbstractValidator<SupplementProductListRequest>
{
    public SupplementProductListRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(s => SupplementRules.IsBlankOrOneOf(s, SupplementRules.ProductStatuses))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class SupplementOrderLineRequestValidator : AbstractValidator<SupplementOrderLineRequest>
{
    public SupplementOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 999).WithMessage("Quantity must be between 1 and 999.");
    }
}

/// <summary>UC-78 checkout: at least one product, quantity ≥ 1, recipient and address for Home Delivery.</summary>
public sealed class PlaceSupplementOrderRequestValidator : AbstractValidator<PlaceSupplementOrderRequest>
{
    public PlaceSupplementOrderRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotNull().WithMessage("The cart must contain at least one product.")
            .Must(items => items is { Count: > 0 }).WithMessage("The cart must contain at least one product.")
            .Must(items => items is null || items.Count <= 50).WithMessage("An order can contain at most 50 products.");
        RuleForEach(x => x.Items).SetValidator(new SupplementOrderLineRequestValidator());
        RuleFor(x => x.FulfillmentType)
            .Must(t => SupplementRules.IsOneOf(t, SupplementRules.FulfillmentTypes))
            .WithMessage("Fulfillment type must be GymPickup or HomeDelivery.");

        When(x => string.Equals(x.FulfillmentType?.Trim(), "HomeDelivery", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.RecipientName)
                .Must(SupplementRules.HasText).WithMessage("The Recipient Name field is required for Home Delivery.");
            RuleFor(x => x.DeliveryAddress)
                .Must(SupplementRules.HasText).WithMessage("The Delivery Address field is required for Home Delivery.");
        });
        RuleFor(x => x.RecipientName).MaximumLength(150);
        RuleFor(x => x.DeliveryAddress).MaximumLength(500);
        RuleFor(x => x.DeliveryNote).MaximumLength(500);
    }
}

public sealed class SupplementOrderListRequestValidator : AbstractValidator<SupplementOrderListRequest>
{
    public SupplementOrderListRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => SupplementRules.IsBlankOrOneOf(s, SupplementRules.OrderStatuses))
            .WithMessage("Status filter is invalid.");
        RuleFor(x => x.FulfillmentType)
            .Must(t => SupplementRules.IsBlankOrOneOf(t, SupplementRules.FulfillmentTypes))
            .WithMessage("Fulfillment type filter is invalid.");
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .OverridePropertyName("From")
            .WithMessage("The start date cannot be after the end date.");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class SupplementOutForDeliveryRequestValidator : AbstractValidator<SupplementOutForDeliveryRequest>
{
    public SupplementOutForDeliveryRequestValidator()
    {
        RuleFor(x => x.CourierName)
            .Must(SupplementRules.HasText).WithMessage("The Courier Name field is required.")
            .MaximumLength(150);
        RuleFor(x => x.TrackingCode)
            .Must(SupplementRules.HasText).WithMessage("The Tracking Code field is required.")
            .MaximumLength(100);
    }
}
