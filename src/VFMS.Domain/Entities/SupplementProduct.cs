using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Supplement product in the catalog (GBR-57–GBR-61).
/// Stock is tracked when StockQuantity has a value: orders take items out of stock, cancellations put them back,
/// and a tracked product switches between Active and OutOfStock with its stock. Null means stock is not tracked.
/// </summary>
public class SupplementProduct : BaseEntity
{
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CatalogCategory { get; set; }
    /// <summary>Manufacturer brand, e.g. "Optimum Nutrition".</summary>
    public string? Brand { get; set; }
    /// <summary>Pack size as sold, e.g. "5 lbs (2.27 kg)" or "120 viên".</summary>
    public string? Size { get; set; }
    public string? Flavour { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Units in the gym; null when stock is not tracked for this product.</summary>
    public int? StockQuantity { get; set; }
    public string? ImageUrl { get; set; }
    public SupplementProductStatus ProductStatus { get; set; } = SupplementProductStatus.Active;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<SupplementOrderItem> OrderItems { get; set; } = [];
}

