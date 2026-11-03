using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Waiting-list entry for a fully-booked <see cref="YogaClassSchedule"/> (GBR-80, GBR-82).
/// </summary>
public class YogaWaitlist : BaseEntity
{
    public int YogaClassScheduleId { get; set; }
    public int? YogaClassTypeId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public YogaWaitlistStatus WaitlistStatus { get; set; } = YogaWaitlistStatus.Waiting;

    // ── Navigation Properties ──────────────────────────────────
    public YogaClassSchedule YogaClassSchedule { get; set; } = null!;
    public YogaClassType? YogaClassType { get; set; }
    public ApplicationUser Member { get; set; } = null!;
}

