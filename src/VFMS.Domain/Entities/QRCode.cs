using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// QR code token used for self check-in at the kiosk (GBR-89).
/// Validates active membership before allowing entry.
/// </summary>
public class QRCode : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string QRToken { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser User { get; set; } = null!;
}

