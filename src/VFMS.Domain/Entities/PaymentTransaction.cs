using System.ComponentModel.DataAnnotations.Schema;
using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Payment transaction record for an online VietQR or counter cash order.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    [NotMapped]
    public string UserId => Order.PurchaserUserId;
    public int OrderId { get; set; }
    public string GatewayName { get; set; } = string.Empty;   // e.g. "SePay", "Cash"
    public string? GatewayTransactionReference { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountReceived { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public string? RawGatewayStatus { get; set; }
    public int? LockerRentalContractId { get; set; }
    public LockerPaymentPurpose? LockerPurpose { get; set; }
    public int? SourcePaymentTransactionId { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? PaymentExpiresAt { get; set; }
    /// <summary>GBR-109: when the Member clicked 'Proceed to Payment' on an online locker rental.</summary>
    public DateTime? PaymentStartedAt { get; set; }
    public DateTime? RentalExpiryAtQuote { get; set; }
    public int? RenewalMonths { get; set; }
    public decimal? OverdueFees { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundReason { get; set; }
    public string? RefundStatus { get; set; }
    public DateTime? RefundResolvedAt { get; set; }
    public PaymentMethod? RefundMethod { get; set; }
    public string? RefundedByUserId { get; set; }
    public string? RefundReference { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    [NotMapped]
    public ApplicationUser User => Order.Purchaser;
    public Order Order { get; set; } = null!;
    public Invoice? Invoice { get; set; }
    public LockerRentalContract? LockerRentalContract { get; set; }
    public PaymentTransaction? SourcePaymentTransaction { get; set; }
}

