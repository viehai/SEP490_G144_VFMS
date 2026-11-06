using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Yoga class definition/template.
/// Min/max participants (1 ≤ min ≤ max, GBR-73); published at least 7 days before first session (GBR-77).
/// </summary>
public class YogaClass : BaseEntity
{
    public int YogaClassTypeId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InstructorUserId { get; set; } = string.Empty;
    public int MinParticipants { get; set; }
    public int MaxParticipants { get; set; }
    /// <summary>Session duration in minutes (45–90, GBR-75).</summary>
    public int DurationMinutes { get; set; }
    public YogaClassStatus ClassStatus { get; set; } = YogaClassStatus.Draft;
    /// <summary>Beginner / Intermediate / Advanced (GBR-73).</summary>
    public string DifficultyLevel { get; set; } = "Beginner";
    public int SessionsPerWeek { get; set; } = 1;
    /// <summary>UTC registration deadline (GBR-73, GBR-78).</summary>
    public DateTime RegistrationDeadline { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsTemplate { get; set; } = false;
    public string? ImageUrl { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public YogaClassType YogaClassType { get; set; } = null!;
    public ApplicationUser Instructor { get; set; } = null!;
    public ICollection<YogaClassSchedule> Schedules { get; set; } = [];
    public ICollection<YogaBooking> Bookings { get; set; } = [];
}

