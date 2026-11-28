using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A Yoga Instructor's request to open a class (GBR-128..GBR-130). The Manager approves it by saving the
/// create-class form filled from it, which creates a Draft <see cref="YogaClass"/>, or rejects it with a reason.
/// </summary>
public class YogaClassOpeningRequest : BaseEntity
{
    public string InstructorUserId { get; set; } = string.Empty;
    public int YogaClassTypeId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Plan level: Beginner / Intermediate / Advanced.</summary>
    public string DifficultyLevel { get; set; } = "Beginner";
    public int SessionsPerWeek { get; set; } = 1;
    public int MinParticipants { get; set; }
    public int MaxParticipants { get; set; }
    public int DurationMinutes { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>Weekdays the instructor wants, comma separated (e.g. "Monday,Wednesday"); count = SessionsPerWeek.</summary>
    public string PreferredDays { get; set; } = string.Empty;
    public TimeOnly PreferredStartTime { get; set; }
    public int? PreferredStudioId { get; set; }
    /// <summary>Instructor's note to the Manager.</summary>
    public string? Note { get; set; }

    public YogaClassRequestStatus RequestStatus { get; set; } = YogaClassRequestStatus.Pending;
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
    /// <summary>The Draft class created when the request was approved.</summary>
    public int? YogaClassId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser Instructor { get; set; } = null!;
    public YogaClassType YogaClassType { get; set; } = null!;
    public YogaStudio? PreferredStudio { get; set; }
    public YogaClass? YogaClass { get; set; }
}
