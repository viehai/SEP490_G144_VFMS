using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-35 Manage Yoga Classes, UC-36 Manage Yoga Class Schedules, UC-37 View Yoga Schedule, UC-41 View Assigned Yoga Classes.
/// Rules (SRS Report 3): GBR-73 class completeness, GBR-74 no studio/instructor overlap, GBR-75 45-90 min and 15 min studio gap,
/// GBR-76 cancelled sessions free the room and instructor, GBR-77 publish at least 7 days before the first session.
/// </summary>
public sealed class YogaClassService : IYogaClassService
{
    /// <summary>
    /// Role allowed to teach yoga. Report 3 lists 7 roles, so yoga instructors are PT accounts for now.
    /// Change this one constant if a separate Yoga Instructor role is added.
    /// </summary>
    public const string InstructorRole = "PT";
    public const int StudioGapMinutes = 15;     // GBR-75
    public const int PublishLeadDays = 7;       // GBR-77

    private const string ClassEntity = nameof(YogaClass);
    private const string TypeEntity = nameof(YogaClassType);
    private const string SessionEntity = nameof(YogaClassSchedule);

    private static readonly YogaClassStatus[] PublishedStatuses = [YogaClassStatus.Open, YogaClassStatus.Full];

    private readonly IYogaRepository _repository;
    private readonly TimeProvider _clock;

    public YogaClassService(IYogaRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    // ── Class types ──────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<YogaClassTypeResponse>>> GetClassTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = await _repository.GetClassTypesAsync(cancellationToken);
        return Result<IReadOnlyList<YogaClassTypeResponse>>.Success(types.Select(ToResponse).ToArray());
    }

    public async Task<Result<YogaClassTypeResponse>> CreateClassTypeAsync(
        YogaClassTypeRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var name = request.TypeName.Trim();
        if (await _repository.ClassTypeNameExistsAsync(name, null, cancellationToken))
            return TypeNameTaken();
        var type = new YogaClassType { TypeName = name, Description = Clean(request.Description) };
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, TypeEntity, ipAddress, newValue: $"Name={name}");
        await AuditedWriter.SaveNewAsync(_repository, type, audit, null, cancellationToken);
        return Result<YogaClassTypeResponse>.Success(ToResponse(type), 201);
    }

    public async Task<Result<YogaClassTypeResponse>> UpdateClassTypeAsync(
        int id, YogaClassTypeRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var type = await _repository.GetClassTypeAsync(id, cancellationToken);
        if (type is null)
            return Result<YogaClassTypeResponse>.Failure("Class type not found.", "YOGA_TYPE_NOT_FOUND", 404);
        var name = request.TypeName.Trim();
        if (await _repository.ClassTypeNameExistsAsync(name, id, cancellationToken))
            return TypeNameTaken();
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, TypeEntity, ipAddress,
            $"Name={type.TypeName}", $"Name={name}");
        type.TypeName = name;
        type.Description = Clean(request.Description);
        type.UpdatedAt = DateTime.UtcNow;
        await AuditedWriter.SaveChangedAsync(_repository, type, audit, null, cancellationToken);
        return Result<YogaClassTypeResponse>.Success(ToResponse(type));
    }

    // ── Classes ──────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<YogaInstructorResponse>>> GetInstructorsAsync(CancellationToken cancellationToken = default)
    {
        var users = await _repository.GetUsersInRoleAsync(InstructorRole, cancellationToken);
        return Result<IReadOnlyList<YogaInstructorResponse>>.Success(
            users.Select(u => new YogaInstructorResponse(u.UserId, u.FullName)).OrderBy(u => u.FullName).ToArray());
    }

    public async Task<Result<IReadOnlyList<YogaClassResponse>>> GetClassesAsync(CancellationToken cancellationToken = default)
    {
        var classes = await _repository.GetClassesAsync(cancellationToken);
        return Result<IReadOnlyList<YogaClassResponse>>.Success(classes.Select(ToResponse).ToArray());
    }

    public async Task<Result<YogaClassResponse>> GetClassAsync(int id, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(id, cancellationToken);
        return yogaClass is null ? ClassNotFound<YogaClassResponse>() : Result<YogaClassResponse>.Success(ToResponse(yogaClass));
    }

    public async Task<Result<YogaClassResponse>> CreateClassAsync(
        YogaClassRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var check = await ValidateClassReferencesAsync(request, cancellationToken);
        if (check is not null)
            return check;
        if (request.StartDate < YogaTime.Today(_clock))
            return Result<YogaClassResponse>.Failure("Start date cannot be in the past.", "YOGA_CLASS_START_PAST");

        var yogaClass = new YogaClass { ClassStatus = YogaClassStatus.Draft };
        Apply(yogaClass, request);
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, ClassEntity, ipAddress, newValue: Describe(yogaClass));
        await AuditedWriter.SaveNewAsync(_repository, yogaClass, audit, null, cancellationToken);
        var saved = await _repository.GetClassAsync(yogaClass.Id, cancellationToken) ?? yogaClass;
        return Result<YogaClassResponse>.Success(ToResponse(saved), 201);
    }

    public async Task<Result<YogaClassResponse>> UpdateClassAsync(
        int id, YogaClassRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(id, cancellationToken);
        if (yogaClass is null)
            return ClassNotFound<YogaClassResponse>();
        if (yogaClass.ClassStatus != YogaClassStatus.Draft)
            return Result<YogaClassResponse>.Failure(
                "Only draft classes can be edited. Cancel the class or edit its sessions instead.", "YOGA_CLASS_NOT_DRAFT", 409);

        var check = await ValidateClassReferencesAsync(request, cancellationToken);
        if (check is not null)
            return check;

        var active = yogaClass.Schedules.Where(s => s.ScheduleStatus != YogaScheduleStatus.Cancelled).ToArray();
        if (active.Any(s => s.ScheduledDate < request.StartDate || s.ScheduledDate > request.EndDate))
            return Result<YogaClassResponse>.Failure(
                "Some sessions fall outside the new start and end dates. Move or cancel them first.", "YOGA_CLASS_SESSIONS_OUTSIDE");
        if (active.Any(s => (int)(s.EndTime - s.StartTime).TotalMinutes != request.DurationMinutes))
            return Result<YogaClassResponse>.Failure(
                "Existing sessions have a different length. Update or cancel them before changing the duration.", "YOGA_CLASS_DURATION_MISMATCH");

        if (request.InstructorUserId.Trim() != yogaClass.InstructorUserId && active.Any(s => s.InstructorUserId is null))
            return Result<YogaClassResponse>.Failure(
                "Existing sessions use the current class instructor. Set the instructor on those sessions or cancel them first.",
                "YOGA_CLASS_INSTRUCTOR_IN_USE", 409);   // GBR-74

        var before = Describe(yogaClass);
        Apply(yogaClass, request);
        yogaClass.UpdatedAt = DateTime.UtcNow;
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, ClassEntity, ipAddress, before, Describe(yogaClass));
        await AuditedWriter.SaveChangedAsync(_repository, yogaClass, audit, null, cancellationToken);
        var saved = await _repository.GetClassAsync(id, cancellationToken) ?? yogaClass;
        return Result<YogaClassResponse>.Success(ToResponse(saved));
    }

    /// <summary>Opens the class for registration (GBR-77: first session at least 7 days away).</summary>
    public async Task<Result<YogaClassResponse>> PublishClassAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(id, cancellationToken);
        if (yogaClass is null)
            return ClassNotFound<YogaClassResponse>();
        if (yogaClass.ClassStatus != YogaClassStatus.Draft)
            return Result<YogaClassResponse>.Failure("Only draft classes can be published.", "YOGA_CLASS_NOT_DRAFT", 409);

        var sessions = yogaClass.Schedules.Where(s => s.ScheduleStatus != YogaScheduleStatus.Cancelled).ToArray();
        if (sessions.Length == 0)
            return Result<YogaClassResponse>.Failure("Add at least one session before publishing.", "YOGA_CLASS_NO_SESSIONS");

        var firstSession = sessions.Min(s => s.ScheduledDate);
        if (firstSession < YogaTime.Today(_clock).AddDays(PublishLeadDays))
            return Result<YogaClassResponse>.Failure(
                $"The timetable must be published at least {PublishLeadDays} days before the first session ({firstSession:yyyy-MM-dd}).",
                "YOGA_PUBLISH_TOO_LATE");
        if (yogaClass.RegistrationDeadline <= _clock.GetUtcNow().UtcDateTime)
            return Result<YogaClassResponse>.Failure("The registration deadline has already passed.", "YOGA_DEADLINE_PASSED");

        var before = Describe(yogaClass);
        yogaClass.ClassStatus = YogaClassStatus.Open;
        yogaClass.UpdatedAt = DateTime.UtcNow;
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, ClassEntity, ipAddress, before, Describe(yogaClass));
        await AuditedWriter.SaveChangedAsync(_repository, yogaClass, audit, null, cancellationToken);
        return Result<YogaClassResponse>.Success(ToResponse(yogaClass));
    }

    public async Task<Result<YogaClassResponse>> CancelClassAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(id, cancellationToken);
        if (yogaClass is null)
            return ClassNotFound<YogaClassResponse>();
        if (yogaClass.ClassStatus is YogaClassStatus.Cancelled or YogaClassStatus.Completed)
            return Result<YogaClassResponse>.Failure("This class is already closed.", "YOGA_CLASS_CLOSED", 409);

        var before = Describe(yogaClass);
        var now = DateTime.UtcNow;
        var today = YogaTime.Today(_clock);
        var notifications = new List<Notification>();
        yogaClass.ClassStatus = YogaClassStatus.Cancelled;
        yogaClass.UpdatedAt = now;
        foreach (var session in yogaClass.Schedules.Where(s => s.ScheduleStatus == YogaScheduleStatus.Scheduled && s.ScheduledDate >= today))
            MarkCancelled(session, now);
        foreach (var booking in yogaClass.Bookings.Where(b => b.BookingStatus == YogaBookingStatus.Confirmed))
        {
            booking.BookingStatus = YogaBookingStatus.Cancelled;
            booking.CancelledAt = now;
            notifications.Add(AuditedWriter.Notify(booking.MemberUserId, actorUserId, "Yoga class cancelled",
                $"{yogaClass.ClassName} has been cancelled by the gym.", ClassEntity, NotificationType.YogaSessionCancelled));
        }

        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, ClassEntity, ipAddress, before, Describe(yogaClass));
        await AuditedWriter.SaveChangedAsync(_repository, yogaClass, audit, notifications, cancellationToken);
        return Result<YogaClassResponse>.Success(ToResponse(yogaClass));
    }

    // ── Sessions ─────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<YogaSessionResponse>>> GetClassSessionsAsync(int classId, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(classId, cancellationToken);
        if (yogaClass is null)
            return ClassNotFound<IReadOnlyList<YogaSessionResponse>>();
        var sessions = yogaClass.Schedules.OrderBy(s => s.ScheduledDate).ThenBy(s => s.StartTime).ToArray();
        var names = await _repository.GetUserNamesAsync(sessions.Select(s => InstructorOf(s, yogaClass)), cancellationToken);
        return Result<IReadOnlyList<YogaSessionResponse>>.Success(
            sessions.Select(s => ToResponse(s, yogaClass, names)).ToArray());
    }

    public async Task<Result<YogaSessionResponse>> CreateSessionAsync(
        YogaSessionRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(request.YogaClassId, cancellationToken);
        if (yogaClass is null)
            return ClassNotFound<YogaSessionResponse>();

        var session = new YogaClassSchedule { YogaClassId = yogaClass.Id, ScheduleStatus = YogaScheduleStatus.Scheduled };
        Result<YogaSessionResponse>? failure = null;
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            // GBR-74/75: overlap check and insert run under one lock, so two managers cannot book the same slot.
            await _repository.LockScheduleDateAsync(request.ScheduledDate, cancellationToken);
            failure = await ValidateSessionAsync(request, yogaClass, null, cancellationToken);
            if (failure is not null)
                return;
            ApplySession(session, request, yogaClass);
            var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, SessionEntity, ipAddress, newValue: Describe(session));
            await AuditedWriter.SaveNewAsync(_repository, session, audit, null, cancellationToken);
        }, cancellationToken);
        return failure ?? await SessionResponseAsync(session.Id, 201, cancellationToken);
    }

    public async Task<Result<YogaSessionResponse>> UpdateSessionAsync(
        int id, YogaSessionRequest request, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetSessionAsync(id, cancellationToken);
        if (session is null)
            return SessionNotFound();
        if (session.YogaClassId != request.YogaClassId)
            return Result<YogaSessionResponse>.Failure("A session cannot be moved to another class.", "YOGA_SESSION_CLASS_CHANGE");
        if (session.ScheduleStatus != YogaScheduleStatus.Scheduled)
            return Result<YogaSessionResponse>.Failure("Only scheduled sessions can be changed.", "YOGA_SESSION_NOT_SCHEDULED", 409);

        Result<YogaSessionResponse>? failure = null;
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            await _repository.LockScheduleDateAsync(request.ScheduledDate, cancellationToken);   // GBR-74/75
            failure = await ValidateSessionAsync(request, session.YogaClass, session, cancellationToken);
            if (failure is not null)
                return;

            var before = Describe(session);
            var moved = session.ScheduledDate != request.ScheduledDate || session.StartTime != request.StartTime ||
                        session.YogaStudioId != request.YogaStudioId;
            ApplySession(session, request, session.YogaClass);
            session.UpdatedAt = DateTime.UtcNow;

            var notifications = moved
                ? ConfirmedMembers(session.YogaClass).Select(memberId => AuditedWriter.Notify(memberId, actorUserId,
                    "Yoga session changed",
                    $"{session.YogaClass.ClassName} is now on {session.ScheduledDate:dd/MM/yyyy} at {session.StartTime:HH\\:mm}.",
                    SessionEntity, NotificationType.YogaSessionChanged)).ToArray()
                : [];
            var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, SessionEntity, ipAddress, before, Describe(session));
            await AuditedWriter.SaveChangedAsync(_repository, session, audit, notifications, cancellationToken);
        }, cancellationToken);
        return failure ?? await SessionResponseAsync(id, 200, cancellationToken);
    }

    /// <summary>GBR-76: a cancelled session no longer holds its studio or instructor; booked members are notified.</summary>
    public async Task<Result<YogaSessionResponse>> CancelSessionAsync(
        int id, string actorUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetSessionAsync(id, cancellationToken);
        if (session is null)
            return SessionNotFound();
        if (session.ScheduleStatus != YogaScheduleStatus.Scheduled)
            return Result<YogaSessionResponse>.Failure("Only scheduled sessions can be cancelled.", "YOGA_SESSION_NOT_SCHEDULED", 409);

        var before = Describe(session);
        MarkCancelled(session, DateTime.UtcNow);
        var notifications = ConfirmedMembers(session.YogaClass).Select(memberId => AuditedWriter.Notify(memberId, actorUserId,
            "Yoga session cancelled",
            $"{session.YogaClass.ClassName} on {session.ScheduledDate:dd/MM/yyyy} at {session.StartTime:HH\\:mm} has been cancelled by the gym.",
            SessionEntity, NotificationType.YogaSessionCancelled)).ToArray();
        var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, SessionEntity, ipAddress, before, Describe(session));
        await AuditedWriter.SaveChangedAsync(_repository, session, audit, notifications, cancellationToken);
        return await SessionResponseAsync(id, 200, cancellationToken);
    }

    // ── Views ────────────────────────────────────────────────────

    /// <summary>UC-37: sessions of published (Open/Full) classes, with live free seats.</summary>
    public async Task<Result<IReadOnlyList<YogaSessionResponse>>> GetPublishedScheduleAsync(
        YogaScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var (from, to) = Range(query);
        if (to < from || to.DayNumber - from.DayNumber > 62)
            return Result<IReadOnlyList<YogaSessionResponse>>.Failure("Choose a date range of up to 62 days.", "YOGA_INVALID_RANGE");

        var sessions = (await _repository.GetSessionsAsync(from, to, cancellationToken))
            .Where(s => s.ScheduleStatus != YogaScheduleStatus.Cancelled && PublishedStatuses.Contains(s.YogaClass.ClassStatus))
            .Where(s => Matches(s, query))
            .ToArray();
        var names = await _repository.GetUserNamesAsync(sessions.Select(s => InstructorOf(s, s.YogaClass)), cancellationToken);
        return Result<IReadOnlyList<YogaSessionResponse>>.Success(sessions.Select(s => ToResponse(s, s.YogaClass, names)).ToArray());
    }

    /// <summary>UC-41: sessions the instructor teaches, with the booked roster.</summary>
    public async Task<Result<IReadOnlyList<YogaAssignedSessionResponse>>> GetAssignedSessionsAsync(
        string instructorUserId, YogaScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var (from, to) = Range(query);
        if (to < from || to.DayNumber - from.DayNumber > 62)
            return Result<IReadOnlyList<YogaAssignedSessionResponse>>.Failure("Choose a date range of up to 62 days.", "YOGA_INVALID_RANGE");

        var sessions = (await _repository.GetSessionsAsync(from, to, cancellationToken))
            .Where(s => InstructorOf(s, s.YogaClass) == instructorUserId && s.YogaClass.ClassStatus != YogaClassStatus.Draft)
            .ToArray();
        var memberIds = sessions.SelectMany(s => s.YogaClass.Bookings).Select(b => b.MemberUserId);
        var names = await _repository.GetUserNamesAsync(memberIds.Append(instructorUserId), cancellationToken);
        var result = sessions.Select(s => new YogaAssignedSessionResponse(
                ToResponse(s, s.YogaClass, names),
                s.YogaClass.Bookings
                    .Where(b => b.BookingStatus != YogaBookingStatus.Cancelled)
                    .OrderBy(b => b.MatNumber ?? int.MaxValue)
                    .Select(b => new YogaRosterEntry(b.Id, b.MemberUserId, names.GetValueOrDefault(b.MemberUserId, b.MemberUserId),
                        b.MatNumber, b.BookingStatus.ToString()))
                    .ToArray()))
            .ToArray();
        return Result<IReadOnlyList<YogaAssignedSessionResponse>>.Success(result);
    }

    // ── Rules ────────────────────────────────────────────────────

    private async Task<Result<YogaClassResponse>?> ValidateClassReferencesAsync(YogaClassRequest request, CancellationToken cancellationToken)
    {
        if (await _repository.GetClassTypeAsync(request.YogaClassTypeId, cancellationToken) is null)
            return Result<YogaClassResponse>.Failure("Class type not found.", "YOGA_TYPE_NOT_FOUND");
        if (!await _repository.UserHasRoleAsync(request.InstructorUserId, InstructorRole, cancellationToken))
            return Result<YogaClassResponse>.Failure("The selected instructor is not a trainer.", "YOGA_INVALID_INSTRUCTOR");
        return null;
    }

    private async Task<Result<YogaSessionResponse>?> ValidateSessionAsync(
        YogaSessionRequest request, YogaClass yogaClass, YogaClassSchedule? existing, CancellationToken cancellationToken)
    {
        if (yogaClass.ClassStatus is YogaClassStatus.Cancelled or YogaClassStatus.Completed)
            return Result<YogaSessionResponse>.Failure("Sessions cannot be added to a closed class.", "YOGA_CLASS_CLOSED", 409);
        if (request.ScheduledDate < yogaClass.StartDate || request.ScheduledDate > yogaClass.EndDate)
            return Result<YogaSessionResponse>.Failure(
                $"The session date must be between {yogaClass.StartDate:yyyy-MM-dd} and {yogaClass.EndDate:yyyy-MM-dd}.",
                "YOGA_SESSION_OUTSIDE_CLASS");
        if (YogaTime.ToUtc(request.ScheduledDate, request.StartTime) <= _clock.GetUtcNow().UtcDateTime)
            return Result<YogaSessionResponse>.Failure("A session cannot start in the past.", "YOGA_SESSION_IN_PAST");
        // GBR-77 only for new or re-dated sessions: a substitute instructor can still be set close to the day.
        if (yogaClass.ClassStatus != YogaClassStatus.Draft && request.ScheduledDate != existing?.ScheduledDate &&
            request.ScheduledDate < YogaTime.Today(_clock).AddDays(PublishLeadDays))
            return Result<YogaSessionResponse>.Failure(
                $"The class is already published. New or moved sessions must be at least {PublishLeadDays} days away.",
                "YOGA_PUBLISH_TOO_LATE");   // GBR-77
        if ((int)(request.EndTime - request.StartTime).TotalMinutes != yogaClass.DurationMinutes)
            return Result<YogaSessionResponse>.Failure(
                $"Sessions of this class last {yogaClass.DurationMinutes} minutes.", "YOGA_SESSION_DURATION");

        var studio = await _repository.GetStudioAsync(request.YogaStudioId, cancellationToken);
        if (studio is null || !studio.IsActive)
            return Result<YogaSessionResponse>.Failure("The studio is not available.", "YOGA_STUDIO_UNAVAILABLE");
        if (studio.Capacity < yogaClass.MaxParticipants)
            return Result<YogaSessionResponse>.Failure(
                $"The studio has {studio.Capacity} mats but the class allows {yogaClass.MaxParticipants} participants.",
                "YOGA_STUDIO_TOO_SMALL");

        var instructorId = string.IsNullOrWhiteSpace(request.InstructorUserId) ? yogaClass.InstructorUserId : request.InstructorUserId.Trim();
        if (instructorId != yogaClass.InstructorUserId &&
            !await _repository.UserHasRoleAsync(instructorId, InstructorRole, cancellationToken))
            return Result<YogaSessionResponse>.Failure("The selected instructor is not a trainer.", "YOGA_INVALID_INSTRUCTOR");

        var sameDay = (await _repository.GetActiveSessionsOnDateAsync(request.ScheduledDate, cancellationToken))
            .Where(s => s.Id != existing?.Id)
            .ToArray();
        var conflict = FindConflict(request.YogaStudioId, instructorId, request.StartTime, request.EndTime, sameDay);
        return conflict is null
            ? null
            : Result<YogaSessionResponse>.Failure(conflict, "YOGA_SESSION_CONFLICT", 409);
    }

    /// <summary>
    /// GBR-74: no overlap in the same studio or with the same instructor. GBR-75: 15-minute gap in the same studio.
    /// Cancelled sessions must already be excluded by the caller (GBR-76).
    /// </summary>
    public static string? FindConflict(
        int studioId, string instructorUserId, TimeOnly start, TimeOnly end, IEnumerable<YogaClassSchedule> sameDaySessions)
    {
        foreach (var other in sameDaySessions)
        {
            if (other.YogaStudioId == studioId &&
                start < other.EndTime.AddMinutes(StudioGapMinutes) && other.StartTime < end.AddMinutes(StudioGapMinutes))
                return $"The studio is in use from {other.StartTime:HH\\:mm} to {other.EndTime:HH\\:mm}. Leave at least {StudioGapMinutes} minutes between sessions.";

            var otherInstructor = other.InstructorUserId ?? other.YogaClass?.InstructorUserId;
            if (otherInstructor == instructorUserId && start < other.EndTime && other.StartTime < end)
                return $"The instructor already teaches from {other.StartTime:HH\\:mm} to {other.EndTime:HH\\:mm}.";
        }
        return null;
    }

    private static void MarkCancelled(YogaClassSchedule session, DateTime now)
    {
        session.ScheduleStatus = YogaScheduleStatus.Cancelled;
        session.CancelledAt = now;
        session.UpdatedAt = now;
    }

    private static IEnumerable<string> ConfirmedMembers(YogaClass yogaClass) =>
        yogaClass.Bookings.Where(b => b.BookingStatus == YogaBookingStatus.Confirmed).Select(b => b.MemberUserId);

    private static bool Matches(YogaClassSchedule s, YogaScheduleQuery query) =>
        (string.IsNullOrWhiteSpace(query.InstructorUserId) || InstructorOf(s, s.YogaClass) == query.InstructorUserId) &&
        (string.IsNullOrWhiteSpace(query.DifficultyLevel) ||
         string.Equals(s.YogaClass.DifficultyLevel, query.DifficultyLevel.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        (query.YogaClassTypeId is null || s.YogaClass.YogaClassTypeId == query.YogaClassTypeId);

    private (DateOnly From, DateOnly To) Range(YogaScheduleQuery query)
    {
        var today = YogaTime.Today(_clock);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var from = query.From ?? monday;
        return (from, query.To ?? from.AddDays(6));
    }

    private async Task<Result<YogaSessionResponse>> SessionResponseAsync(int id, int statusCode, CancellationToken cancellationToken)
    {
        var session = await _repository.GetSessionAsync(id, cancellationToken);
        if (session is null)
            return SessionNotFound();
        var names = await _repository.GetUserNamesAsync([InstructorOf(session, session.YogaClass)], cancellationToken);
        return Result<YogaSessionResponse>.Success(ToResponse(session, session.YogaClass, names), statusCode);
    }

    // ── Mapping ──────────────────────────────────────────────────

    private static string InstructorOf(YogaClassSchedule s, YogaClass c) => s.InstructorUserId ?? c.InstructorUserId;

    private static void Apply(YogaClass c, YogaClassRequest r)
    {
        c.YogaClassTypeId = r.YogaClassTypeId;
        c.ClassName = r.ClassName.Trim();
        c.Description = Clean(r.Description);
        c.InstructorUserId = r.InstructorUserId.Trim();
        c.DifficultyLevel = Normalize(r.DifficultyLevel);
        c.SessionsPerWeek = r.SessionsPerWeek;
        c.MinParticipants = r.MinParticipants;
        c.MaxParticipants = r.MaxParticipants;
        c.DurationMinutes = r.DurationMinutes;
        c.RegistrationDeadline = DateTime.SpecifyKind(r.RegistrationDeadline.ToUniversalTime(), DateTimeKind.Utc);
        c.StartDate = r.StartDate;
        c.EndDate = r.EndDate;
        c.ImageUrl = Clean(r.ImageUrl);
    }

    private static void ApplySession(YogaClassSchedule s, YogaSessionRequest r, YogaClass c)
    {
        s.YogaStudioId = r.YogaStudioId;
        s.ScheduledDate = r.ScheduledDate;
        s.StartTime = r.StartTime;
        s.EndTime = r.EndTime;
        var instructor = string.IsNullOrWhiteSpace(r.InstructorUserId) ? null : r.InstructorUserId.Trim();
        s.InstructorUserId = instructor == c.InstructorUserId ? null : instructor;
    }

    private static string Normalize(string difficulty) =>
        Contracts.Yoga.Validators.YogaRules.DifficultyLevels.First(d => d.Equals(difficulty.Trim(), StringComparison.OrdinalIgnoreCase));

    private static YogaClassTypeResponse ToResponse(YogaClassType t) => new(t.Id, t.TypeName, t.Description);

    private static YogaClassResponse ToResponse(YogaClass c) => new(
        c.Id, c.YogaClassTypeId, c.YogaClassType?.TypeName ?? string.Empty, c.ClassName, c.Description,
        c.InstructorUserId, c.Instructor?.FullName ?? string.Empty, c.DifficultyLevel, c.SessionsPerWeek,
        c.MinParticipants, c.MaxParticipants, c.DurationMinutes, c.RegistrationDeadline, c.StartDate, c.EndDate,
        c.ClassStatus.ToString(), c.ImageUrl, c.Schedules.Count(s => s.ScheduleStatus != YogaScheduleStatus.Cancelled));

    private static YogaSessionResponse ToResponse(YogaClassSchedule s, YogaClass c, IReadOnlyDictionary<string, string> names)
    {
        var instructor = InstructorOf(s, c);
        var confirmed = c.Bookings.Count(b => b.BookingStatus == YogaBookingStatus.Confirmed);
        return new YogaSessionResponse(
            s.Id, c.Id, c.ClassName, c.YogaClassType?.TypeName ?? string.Empty, c.DifficultyLevel,
            s.YogaStudioId, s.YogaStudio?.StudioName ?? string.Empty, s.YogaStudio?.FloorNumber ?? 0,
            instructor, names.GetValueOrDefault(instructor, string.Empty),
            s.ScheduledDate, s.StartTime, s.EndTime, s.ScheduleStatus.ToString(), c.ClassStatus.ToString(),
            c.MaxParticipants, confirmed, Math.Max(0, c.MaxParticipants - confirmed));
    }

    private static string Describe(YogaClass c) =>
        $"Name={c.ClassName}; Type={c.YogaClassTypeId}; Instructor={c.InstructorUserId}; Difficulty={c.DifficultyLevel}; " +
        $"Participants={c.MinParticipants}-{c.MaxParticipants}; Duration={c.DurationMinutes}; " +
        $"Dates={c.StartDate:yyyy-MM-dd}..{c.EndDate:yyyy-MM-dd}; Deadline={c.RegistrationDeadline:O}; Status={c.ClassStatus}";

    private static string Describe(YogaClassSchedule s) =>
        $"Class={s.YogaClassId}; Studio={s.YogaStudioId}; Date={s.ScheduledDate:yyyy-MM-dd}; " +
        $"Time={s.StartTime:HH\\:mm}-{s.EndTime:HH\\:mm}; Instructor={s.InstructorUserId ?? "(class)"}; Status={s.ScheduleStatus}";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<T> ClassNotFound<T>() => Result<T>.Failure("Yoga class not found.", "YOGA_CLASS_NOT_FOUND", 404);

    private static Result<YogaSessionResponse> SessionNotFound() =>
        Result<YogaSessionResponse>.Failure("Yoga session not found.", "YOGA_SESSION_NOT_FOUND", 404);

    private static Result<YogaClassTypeResponse> TypeNameTaken() =>
        Result<YogaClassTypeResponse>.Failure("A class type with this name already exists.", "YOGA_TYPE_NAME_TAKEN", 409);
}
