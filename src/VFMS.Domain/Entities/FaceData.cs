using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Stores encrypted facial embeddings for biometric kiosk check-in (GBR-89, GBR-90).
/// Only encrypted FaceNet embeddings are stored; raw camera images are discarded immediately.
/// Embeddings are purged 90 days after the last membership expires (JOB-07, PDPL compliance).
/// </summary>
public class FaceData : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    /// <summary>AES-256 encrypted FaceNet embedding vector (PDPL compliant).</summary>
    public byte[] FaceNetEmbedding { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    /// <summary>Explicit PDPL consent timestamp.</summary>
    public DateTime ConsentGrantedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser User { get; set; } = null!;
}

