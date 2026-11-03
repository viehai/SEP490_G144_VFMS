using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Immutable financial invoice generated upon successful payment (GBR-33).
/// Must never be hard-deleted or modified after issuance.
/// </summary>
public class Invoice : BaseEntity
{
    public int PaymentTransactionId { get; set; }
    /// <summary>Unique human-readable invoice number (e.g. INV-20240101-0001).</summary>
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public InvoiceStatus InvoiceStatus { get; set; } = InvoiceStatus.Issued;

    // ── Navigation Properties ──────────────────────────────────
    public PaymentTransaction PaymentTransaction { get; set; } = null!;
}

