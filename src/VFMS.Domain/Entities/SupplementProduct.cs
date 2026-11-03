using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Supplement product in the catalog (GBR-57–GBR-61).
/// No stock inventory tracking; catalog-only display.
/// </summary>
public class SupplementProduct : BaseEntity
{
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CatalogCategory { get; set; }
    public decimal UnitPrice { get; set; }
    public string? ImageUrl { get; set; }
    public SupplementProductStatus ProductStatus { get; set; } = SupplementProductStatus.Active;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<SupplementOrderItem> OrderItems { get; set; } = [];
}

