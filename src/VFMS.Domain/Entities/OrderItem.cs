using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Line item within an <see cref="Order"/>.
/// Supports purchasing for oneself or gifting to another registered user (GBR-36).
/// Activation start date can be immediate or deferred up to 30 days (GBR-35).
/// </summary>
public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public int PackageId { get; set; }
    /// <summary>The user who benefits from this item (may differ from purchaser for gifting).</summary>
    public string BeneficiaryUserId { get; set; } = string.Empty;
    /// <summary>Snapshot of the unit price at time of purchase (financial immutability, GBR-33).</summary>
    public decimal UnitPriceSnapshot { get; set; }
    /// <summary>Snapshot of the package name at time of purchase.</summary>
    public string PackageNameSnapshot { get; set; } = string.Empty;
    /// <summary>Requested activation start date (null = immediate).</summary>
    public DateOnly? ActivationStartDate { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Order Order { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public ApplicationUser Beneficiary { get; set; } = null!;
    public ICollection<MemberMembership> MemberMemberships { get; set; } = [];
}

