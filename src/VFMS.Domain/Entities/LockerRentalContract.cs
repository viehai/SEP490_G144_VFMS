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
    public decimal DepositAmount { get; set; }
    public decimal RentalPrice { get; set; }
    public RentalType? RentalTypeSnapshot { get; set; }
    public decimal? OverdueDailyFeeSnapshot { get; set; }
    public decimal? KeyReplacementFeeSnapshot { get; set; }
    public DateTime? HandedOverAt { get; set; }
    public string? HandedOverByUserId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CollectBy { get; set; }
    public DateTime? FinalNoticeAt { get; set; }
    public DateTime? FinalNoticeEmailSentAt { get; set; }
    public DateTime? FinalNoticeRetryAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public string? SettledByUserId { get; set; }
    public decimal? OutstandingAmount { get; set; }
    public LockerContractStatus ContractStatus { get; set; } = LockerContractStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public string? Note { get; set; }
    /// <summary>Amount forfeited or refunded upon termination.</summary>
    public decimal? FineAmount { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Locker Locker { get; set; } = null!;
    public LockerPricingPolicy LockerPricingPolicy { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
    public ICollection<LockerRentalPayment> Payments { get; set; } = [];
}

