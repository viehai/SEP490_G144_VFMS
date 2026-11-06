using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A member's booking for a specific <see cref="YogaClassSchedule"/>.
/// Requires active Yoga/Combo membership (MSG-31), max 1 booking per class (GBR-79).
/// Auto-waitlist when full (GBR-80); free cancellation ≥ 2 hours before start (GBR-83).
/// Auto 7-day suspension after 3 no-shows within 30 days (GBR-84).
/// </summary>
public class YogaBooking : BaseEntity
{
    public int YogaClassScheduleId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    /// <summary>Reserved mat position on the studio floorplan (UC-38, MSG-33).</summary>
    public int? MatNumber { get; set; }
    public DateTime BookedAt { get; set; }
    public YogaBookingStatus BookingStatus { get; set; } = YogaBookingStatus.Confirmed;
    public DateTime? CancelledAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public YogaClassSchedule YogaClassSchedule { get; set; } = null!;
    public ApplicationUser Member { get; set; } = null!;
    public YogaAttendance? Attendance { get; set; }
}

