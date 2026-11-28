namespace VFMS.Contracts.Yoga;

// ── Class opening requests (GBR-128..GBR-130) ───────────────────
// A Yoga Instructor asks the Manager to open a class. The Manager approves by saving the create-class
// form filled from the request (body = YogaClassRequest), or rejects with a reason.

/// <summary>What the instructor asks for. Dates are gym-local; PreferredDays count must equal SessionsPerWeek.</summary>
public sealed record YogaClassOpeningRequestRequest(
    int YogaClassTypeId,
    string ClassName,
    string DifficultyLevel,
    int SessionsPerWeek,
    int MinParticipants,
    int MaxParticipants,
    int DurationMinutes,
    DateOnly StartDate,
    DateOnly EndDate,
    DayOfWeek[] PreferredDays,
    TimeOnly PreferredStartTime,
    int? PreferredStudioId = null,
    string? Description = null,
    string? Note = null
);

public sealed record YogaClassOpeningRequestResponse(
    int Id,
    string InstructorUserId,
    string InstructorName,
    int YogaClassTypeId,
    string TypeName,
    string ClassName,
    string? Description,
    string DifficultyLevel,
    int SessionsPerWeek,
    int MinParticipants,
    int MaxParticipants,
    int DurationMinutes,
    DateOnly StartDate,
    DateOnly EndDate,
    DayOfWeek[] PreferredDays,
    TimeOnly PreferredStartTime,
    int? PreferredStudioId,
    string? PreferredStudioName,
    string? Note,
    /// <summary>Pending, Approved, Rejected or Withdrawn.</summary>
    string Status,
    string? RejectReason,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    int? YogaClassId,
    DateTime CreatedAt
);

public sealed record RejectYogaClassOpeningRequest(string Reason);

/// <summary>Dropdown data for the instructor's request form: class types and active studios.</summary>
public sealed record YogaClassOpeningOptionsResponse(
    IReadOnlyList<YogaClassTypeResponse> ClassTypes,
    IReadOnlyList<YogaStudioResponse> Studios
);
