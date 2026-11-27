using System.ComponentModel.DataAnnotations.Schema;
using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Locker rental contract between a member and the gym.
/// Overdue > 7 days incurs deposit forfeiture and forced clearance (GBR-20).
/// Key deposit of 100,000 VND is refundable upon proper termination (GBR-19).
/// </summary>
public class LockerRentalContract : BaseEntity
{
    public int LockerId { get; set; }
    public int LockerPricingPolicyId { get; set; }
    public string CustomerUserId { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [NotMapped]
    public decimal DepositAmount => LockerPricingPolicy.DepositAmount;
    [NotMapped]
    public decimal RentalPrice => LockerPricingPolicy.RentalType == RentalType.Daily ? 0 : LockerPricingPolicy.RentalPrice;
    [NotMapped]
    public RentalType? RentalTypeSnapshot => LockerPricingPolicy.RentalType;
    [NotMapped]
    public int? TermMonthsSnapshot => LockerPricingPolicy.DurationMonths;
    [NotMapped]
    public decimal? OverdueDailyFeeSnapshot => LockerPricingPolicy.OverdueDailyFee;
    public DateTime? HandedOverAt { get; set; }
    public string? HandedOverByUserId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CollectBy { get; set; }
    public DateTime? FinalNoticeAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    [NotMapped]
    public DateTime? SettledAt => ContractStatus is LockerContractStatus.Terminated or LockerContractStatus.Abandoned
        ? CancelledAt : null;
    public string? SettledByUserId { get; set; }
    public decimal? OutstandingAmount { get; set; }
    /// <summary>GBR-110: when Staff collected the OutstandingAmount; null while it is still unpaid.</summary>
    public DateTime? OutstandingPaidAt { get; set; }
    /// <summary>GBR-20: when the in-app reminder 7 days before expiry was sent.</summary>
    public DateTime? ReminderSentAt { get; set; }
    public DateTime? KeyLossConfirmedAt { get; set; }
    public int? ReplacementLockerId { get; set; }
    public Locker? ReplacementLocker { get; set; }
    public LockerContractStatus ContractStatus { get; set; } = LockerContractStatus.Active;
    public DateTime? CancelledAt { get; set; }
    /// <summary>Amount forfeited or refunded upon termination.</summary>
    public decimal? FineAmount { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Locker Locker { get; set; } = null!;
    public LockerPricingPolicy LockerPricingPolicy { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
    public ICollection<PaymentTransaction> Payments { get; set; } = [];
}

