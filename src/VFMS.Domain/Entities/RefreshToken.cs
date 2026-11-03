using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// JWT refresh token. Blacklisted immediately upon logout (GBR-28).
/// Tokens expire in 7 days (30 days with Remember Me).
/// </summary>
public class RefreshToken : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }
    public bool RememberMe { get; set; } = false;
    public string? CreatedByIp { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser User { get; set; } = null!;
}

