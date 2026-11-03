using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Gym equipment item.
/// Members report issues (UC-71); Staff handles (UC-72); Manager manages inventory (GBR-53–GBR-56).
/// </summary>
public class Equipment : BaseEntity
{
    public int GymId { get; set; }
    public int? CatalogCategoryId { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string? EquipmentCode { get; set; }
    public string? EquipmentModel { get; set; }
    public string? EquipmentBrand { get; set; }
    public EquipmentStatus EquipmentStatus { get; set; } = EquipmentStatus.Operational;
    public string? EquipmentDescriptionUrl { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public string? Note { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Gym Gym { get; set; } = null!;
    public CatalogCategory? CatalogCategory { get; set; }
    public ICollection<EquipmentIssueReport> IssueReports { get; set; } = [];
    public ICollection<EquipmentSafetyConfirmation> SafetyConfirmations { get; set; } = [];
}

