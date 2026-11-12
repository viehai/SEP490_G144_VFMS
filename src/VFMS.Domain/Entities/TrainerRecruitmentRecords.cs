namespace VFMS.Domain.Entities;

// JSON value objects, NOT EF entities or tables.
public sealed class TrainerStoredFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? ReplacesCertificateId { get; set; }
    // Preserve old binary files during the forward migration; new uploads use private storage.
    public string? LegacyContentBase64 { get; set; }
    public bool IsPublic { get; set; }
    public bool IsAvatar { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class TrainerQueuedEmail
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string ProtectedBody { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public bool IsDeleted { get; set; }
}
