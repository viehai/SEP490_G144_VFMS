using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A member's enrolment in a whole <see cref="YogaClass"/> (course), per the ERD. Attendance is recorded per session.
/// Requires active Yoga/Combo membership (MSG-31), max 1 booking per class (GBR-79).
/// Auto-waitlist when full (GBR-80); free cancellation ≥ 2 hours before start (GBR-83).
/// Auto 7-day suspension after 3 no-shows within 30 days (GBR-84).
/// </summary>
public class YogaBooking : BaseEntity
{
    public int YogaClassId { get; set; }
    /// <summary>The purchased Yoga package line that pays for this enrolment.</summary>
    public int? OrderItemId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    /// <summary>The member's mat position for the course (UC-38, MSG-33).</summary>
    public int? MatNumber { get; set; }
    public DateTime BookedAt { get; set; }
    public YogaBookingStatus BookingStatus { get; set; } = YogaBookingStatus.Confirmed;
    public DateTime? CancelledAt { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public YogaClass YogaClass { get; set; } = null!;
    public OrderItem? OrderItem { get; set; }
    public ApplicationUser Member { get; set; } = null!;
    public ICollection<YogaAttendance> Attendances { get; set; } = [];
}

