using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Pricing policy for locker rental.
/// </summary>
public class LockerPricingPolicy : BaseEntity
{
    public string PolicyName { get; set; } = string.Empty;
    public RentalType RentalType { get; set; }
    public decimal RentalPrice { get; set; }
    /// <summary>Refundable key deposit (100,000 VND per GBR-19).</summary>
    public decimal DepositAmount { get; set; }
    public PolicyStatus PolicyStatus { get; set; } = PolicyStatus.Active;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<Locker> Lockers { get; set; } = [];
    public ICollection<LockerRentalContract> Contracts { get; set; } = [];
}

