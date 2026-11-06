namespace VFMS.Contracts.Yoga;

// ── Studios (UC-34 Manage Yoga Studios) ─────────────────────────

/// <summary>Create or update a studio. Capacity is the number of mats on the floorplan.</summary>
public sealed record YogaStudioRequest(string StudioName, int FloorNumber, int Capacity);

public sealed record YogaStudioResponse(int Id, string StudioName, int FloorNumber, int Capacity, bool IsActive);

// ── Class types & classes (UC-35 Manage Yoga Classes) ───────────

public sealed record YogaClassTypeRequest(string TypeName, string? Description = null);

public sealed record YogaClassTypeResponse(int Id, string TypeName, string? Description);

/// <summary>
/// A class offering. Difficulty: Beginner, Intermediate, Advanced.
/// RegistrationDeadline is a UTC instant; StartDate/EndDate are gym-local dates.
/// </summary>
public sealed record YogaClassRequest(
    int YogaClassTypeId,
    string ClassName,
    string InstructorUserId,
    string DifficultyLevel,
    int SessionsPerWeek,
    int MinParticipants,
    int MaxParticipants,
    int DurationMinutes,
    DateTime RegistrationDeadline,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description = null,
    string? ImageUrl = null
);

public sealed record YogaClassResponse(
    int Id,
    int YogaClassTypeId,
    string TypeName,
    string ClassName,
    string? Description,
    string InstructorUserId,
    string InstructorName,
    string DifficultyLevel,
    int SessionsPerWeek,
    int MinParticipants,
    int MaxParticipants,
    int DurationMinutes,
    DateTime RegistrationDeadline,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    string? ImageUrl,
    int SessionCount
);

// ── Sessions (UC-36 Manage Yoga Class Schedules) ────────────────

/// <summary>A session of a class. InstructorUserId null means the class instructor.</summary>
public sealed record YogaSessionRequest(
    int YogaClassId,
    int YogaStudioId,
    DateOnly ScheduledDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? InstructorUserId = null
);

public sealed record YogaSessionResponse(
    int Id,
    int YogaClassId,
    string ClassName,
    string TypeName,
    string DifficultyLevel,
    int YogaStudioId,
    string StudioName,
    int FloorNumber,
    string InstructorUserId,
    string InstructorName,
    DateOnly ScheduledDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string Status,
    string ClassStatus,
    int MaxParticipants,
    int ConfirmedBookings,
    int FreeSeats
);

/// <summary>Filter for timetable views. Dates are gym-local; defaults to the current week.</summary>
public sealed record YogaScheduleQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string? InstructorUserId = null,
    string? DifficultyLevel = null,
    int? YogaClassTypeId = null
);

/// <summary>Selectable instructor for class/session forms.</summary>
public sealed record YogaInstructorResponse(string UserId, string FullName);

/// <summary>A member booked on a session (UC-41 roster).</summary>
public sealed record YogaRosterEntry(int BookingId, string MemberUserId, string MemberName, int? MatNumber, string BookingStatus);

public sealed record YogaAssignedSessionResponse(YogaSessionResponse Session, IReadOnlyList<YogaRosterEntry> Roster);
