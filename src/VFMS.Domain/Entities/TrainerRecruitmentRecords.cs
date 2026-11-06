using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

public class TrainerDocument : BaseEntity
{
    public int? TrainerApplicationId { get; set; }
    public int? TrainerProfileId { get; set; }
    public int? TrainerCredentialChangeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public bool IsPublic { get; set; }
    public bool IsAvatar { get; set; }
}

public class TrainerCredentialChange : BaseEntity
{
    public int TrainerProfileId { get; set; }
    public string Specialty { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}

public class TrainerRecruitmentRestriction : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string CitizenId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Reason { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = string.Empty;
}

public class TrainerSetupToken : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

public class TrainerEmail : BaseEntity
{
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string ProtectedBody { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
