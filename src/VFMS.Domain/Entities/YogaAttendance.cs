using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Attendance record for a Yoga session.
/// Instructor records attendance within 30 minutes of session start (GBR-86, GBR-87, UC-51).
/// </summary>
public class YogaAttendance : BaseEntity
{
    public int YogaClassScheduleId { get; set; }
    public int YogaBookingId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public string InstructorUserId { get; set; } = string.Empty;
    public YogaAttendanceStatus AttendanceStatus { get; set; }
    public DateTime RecordedAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public YogaClassSchedule YogaClassSchedule { get; set; } = null!;
    public YogaBooking YogaBooking { get; set; } = null!;
    public ApplicationUser Member { get; set; } = null!;
    public ApplicationUser Instructor { get; set; } = null!;
}

