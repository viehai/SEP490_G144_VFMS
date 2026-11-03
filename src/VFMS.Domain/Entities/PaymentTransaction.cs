using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Payment transaction record for an online VietQR or counter cash order.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string GatewayName { get; set; } = string.Empty;   // e.g. "SePay", "Cash"
    public string? GatewayTransactionReference { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountReceived { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public string? RawGatewayStatus { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser User { get; set; } = null!;
    public Order Order { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}

