using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A scheduled occurrence of a <see cref="YogaClass"/> in a <see cref="YogaStudio"/>.
/// Enforces 15-minute studio buffer between consecutive sessions (GBR-75) and no studio/instructor overlap (GBR-74).
/// Rescheduled sessions link back via <see cref="RescheduledFromId"/>.
/// </summary>
public class YogaClassSchedule : BaseEntity
{
    public int YogaClassId { get; set; }
    public int YogaStudioId { get; set; }
    /// <summary>Instructor for this session; null means the class instructor (GBR-74, UC-45).</summary>
    public string? InstructorUserId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public YogaScheduleStatus ScheduleStatus { get; set; } = YogaScheduleStatus.Scheduled;
    /// <summary>References the original schedule if this is a reschedule (GBR-77).</summary>
    public int? RescheduledFromId { get; set; }
    public DateTime? CancelledAt { get; set; }
    /// <summary>When the attendance roster was submitted; the session is then closed for editing (UC-42, GBR-86).</summary>
    public DateTime? AttendanceSubmittedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public YogaClass YogaClass { get; set; } = null!;
    public YogaStudio YogaStudio { get; set; } = null!;
    public YogaClassSchedule? RescheduledFrom { get; set; }
    public ICollection<YogaClassSchedule> RescheduledSessions { get; set; } = [];
    public ICollection<YogaWaitlist> Waitlists { get; set; } = [];
    public ICollection<YogaAttendance> Attendances { get; set; } = [];
}

