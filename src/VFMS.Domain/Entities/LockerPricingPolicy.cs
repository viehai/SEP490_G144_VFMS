using System.ComponentModel.DataAnnotations.Schema;
using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Pricing policy for locker rental.
/// </summary>
public class LockerPricingPolicy : BaseEntity
{
    public string PolicyName { get; set; } = string.Empty;
    [NotMapped]
    public RentalType RentalType
    {
        get => DurationMonths switch
        {
            0 => RentalType.Daily, 3 => RentalType.Quarterly, 6 => RentalType.HalfYearly,
            12 => RentalType.Yearly, _ => RentalType.Monthly
        };
        set => DurationMonths = value switch
        {
            RentalType.Daily => 0, RentalType.Quarterly => 3, RentalType.HalfYearly => 6,
            RentalType.Yearly => 12, _ => 1
        };
    }
    /// <summary>Rental duration in months; zero identifies a daily rental.</summary>
    public int? DurationMonths { get; set; } = 1;
    public decimal RentalPrice { get; set; }
    /// <summary>Refundable key deposit (100,000 VND per GBR-19).</summary>
    public decimal DepositAmount { get; set; }
    public decimal OverdueDailyFee { get; set; }
    public PolicyStatus PolicyStatus { get; set; } = PolicyStatus.Active;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<LockerRentalContract> Contracts { get; set; } = [];
}

