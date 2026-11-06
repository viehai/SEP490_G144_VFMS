using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IYogaRepository : IAuditedRepository
{
    Task<int?> GetDefaultGymIdAsync(CancellationToken cancellationToken = default);

    // Studios
    Task<IReadOnlyList<YogaStudio>> GetStudiosAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<YogaStudio?> GetStudioAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> StudioNameExistsAsync(int gymId, string name, int? excludingId, CancellationToken cancellationToken = default);
    /// <summary>Largest MaxParticipants among classes with upcoming sessions in the studio (0 if none).</summary>
    Task<int> GetLargestClassUsingStudioAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default);
    Task<bool> StudioHasFutureSessionsAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default);

    // Class types
    Task<IReadOnlyList<YogaClassType>> GetClassTypesAsync(CancellationToken cancellationToken = default);
    Task<YogaClassType?> GetClassTypeAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ClassTypeNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default);

    // Classes
    Task<IReadOnlyList<YogaClass>> GetClassesAsync(CancellationToken cancellationToken = default);
    /// <summary>Class with type, instructor and sessions loaded.</summary>
    Task<YogaClass?> GetClassAsync(int id, CancellationToken cancellationToken = default);

    // Sessions
    /// <summary>Session with class (type, instructor), studio and bookings loaded.</summary>
    Task<YogaClassSchedule?> GetSessionAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Non-cancelled sessions on a date, for overlap checks (GBR-74, GBR-75).</summary>
    Task<IReadOnlyList<YogaClassSchedule>> GetActiveSessionsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default);
    /// <summary>Exclusive lock on one date's timetable until the current transaction ends (GBR-74/75 check-then-insert).</summary>
    Task LockScheduleDateAsync(DateOnly date, CancellationToken cancellationToken = default);
    /// <summary>Sessions in a date range with class, studio and bookings loaded.</summary>
    Task<IReadOnlyList<YogaClassSchedule>> GetSessionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Users
    Task<IReadOnlyList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default);
    Task<bool> UserHasRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
}

/// <summary>UC-34 Manage Yoga Studios.</summary>
public interface IYogaStudioService
{
    Task<Result<IReadOnlyList<YogaStudioResponse>>> GetStudiosAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<Result<YogaStudioResponse>> CreateAsync(YogaStudioRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaStudioResponse>> UpdateAsync(int id, YogaStudioRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaStudioResponse>> SetActiveAsync(int id, bool isActive, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
}

/// <summary>UC-35 Manage Yoga Classes, UC-36 Manage Yoga Class Schedules, UC-37 View Yoga Schedule, UC-41 View Assigned Yoga Classes.</summary>
public interface IYogaClassService
{
    // Class types
    Task<Result<IReadOnlyList<YogaClassTypeResponse>>> GetClassTypesAsync(CancellationToken cancellationToken = default);
    Task<Result<YogaClassTypeResponse>> CreateClassTypeAsync(YogaClassTypeRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaClassTypeResponse>> UpdateClassTypeAsync(int id, YogaClassTypeRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

    // Classes
    Task<Result<IReadOnlyList<YogaInstructorResponse>>> GetInstructorsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<YogaClassResponse>>> GetClassesAsync(CancellationToken cancellationToken = default);
    Task<Result<YogaClassResponse>> GetClassAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<YogaClassResponse>> CreateClassAsync(YogaClassRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaClassResponse>> UpdateClassAsync(int id, YogaClassRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaClassResponse>> PublishClassAsync(int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaClassResponse>> CancelClassAsync(int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

    // Sessions
    Task<Result<IReadOnlyList<YogaSessionResponse>>> GetClassSessionsAsync(int classId, CancellationToken cancellationToken = default);
    Task<Result<YogaSessionResponse>> CreateSessionAsync(YogaSessionRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaSessionResponse>> UpdateSessionAsync(int id, YogaSessionRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<YogaSessionResponse>> CancelSessionAsync(int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default);

    // Views
    Task<Result<IReadOnlyList<YogaSessionResponse>>> GetPublishedScheduleAsync(YogaScheduleQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<YogaAssignedSessionResponse>>> GetAssignedSessionsAsync(string instructorUserId, YogaScheduleQuery query, CancellationToken cancellationToken = default);
}
