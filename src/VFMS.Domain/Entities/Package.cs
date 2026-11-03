using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Commercial package that customers/members can purchase (Gym, Yoga, PT, Combo).
/// Packages with historical orders or active memberships must never be hard-deleted (GBR-33);
/// toggle <see cref="PackageStatus"/> to <c>Inactive</c> instead.
/// </summary>
public class Package : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public PackageStatus PackageStatus { get; set; } = PackageStatus.Active;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<PackageComponent> Components { get; set; } = [];
    public ICollection<OrderItem> OrderItems { get; set; } = [];
}

