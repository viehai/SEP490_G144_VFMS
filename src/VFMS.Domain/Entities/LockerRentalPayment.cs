using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

public class LockerRentalPayment : BaseEntity
{
    public int LockerRentalContractId { get; set; }
    public int OrderId { get; set; }
    public int? SourceOrderId { get; set; }
    public LockerPaymentPurpose Purpose { get; set; }
    public DateTime HoldExpiresAt { get; set; }
    public DateTime? RentalExpiryAtQuote { get; set; }
    public int? RenewalMonths { get; set; }
    public decimal OverdueFees { get; set; }
    public decimal KeyReplacementFee { get; set; }
    public decimal RefundAmount { get; set; }
    public string? RefundReason { get; set; }
    public PaymentMethod? RefundMethod { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? RefundedByUserId { get; set; }
    public string? RefundReference { get; set; }

    public LockerRentalContract Rental { get; set; } = null!;
    public Order Order { get; set; } = null!;
}
