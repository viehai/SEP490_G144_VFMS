using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Phân loại danh mục thiết bị phòng gym (CatalogCategory theo ERD).
/// </summary>
public class CatalogCategory : BaseEntity
{
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<Equipment> Equipments { get; set; } = [];
}

