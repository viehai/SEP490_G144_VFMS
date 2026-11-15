using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-42 Take Yoga Attendance. The session's instructor (session instructor, else class instructor) or any Staff marks
/// each booked member Attended or No-show, or a member scans their QR code; Submit Roster closes the session.
/// Booking status is not changed: the result lives in YogaAttendance, one row per booking and session.
/// </summary>
public sealed class YogaAttendanceService : IYogaAttendanceService
{
    public static readonly TimeSpan SubmitWithin = TimeSpan.FromMinutes(30);   // GBR-86
    public static readonly TimeSpan OpensBefore = TimeSpan.FromMinutes(30);    // "has started or is about to start"

    private readonly IYogaBookingRepository _repository;
    private readonly IMemberQrService _qr;
    private readonly TimeProvider _clock;

    public YogaAttendanceService(IYogaBookingRepository repository, IMemberQrService qr, TimeProvider clock)
    {
        _repository = repository;
        _qr = qr;
        _clock = clock;
    }

    /// <summary>GBR-86: shown to the Manager on the session list.</summary>
    public static bool SubmittedLate(YogaClassSchedule s) =>
        s.AttendanceSubmittedAt is { } at && at - Start(s) > SubmitWithin;

    public async Task<Result<YogaAttendanceRosterResponse>> GetRosterAsync(
        int sessionId, string userId, bool isStaff, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetSessionAsync(sessionId, cancellationToken);
        return CanOpen(session, userId, isStaff)
            ? Result<YogaAttendanceRosterResponse>.Success(ToResponse(session!))
            : NotFound();
    }

    public Task<Result<YogaAttendanceRosterResponse>> ScanQrAsync(
        int sessionId, string userId, bool isStaff, YogaAttendanceQrRequest request, CancellationToken cancellationToken = default) =>
        WriteAsync(sessionId, userId, isStaff, async (session, now) =>
        {
            // GBR-88: a scan counts only for a member booked into this session; otherwise MSG-36 and nothing is marked.
            var memberId = await _qr.RedeemAsync(request.QrToken, cancellationToken);
            var booking = Roster(session).FirstOrDefault(b => b.MemberUserId == memberId);
            if (booking is null)
                return Result<YogaAttendanceRosterResponse>.Failure(GymAccessMessages.Denied, GymAccessMessages.DeniedCode, 403);
            Mark(session, booking, YogaAttendanceStatus.Present, now);
            await _repository.SaveChangesAsync(cancellationToken);
            return Result<YogaAttendanceRosterResponse>.Success(ToResponse(session, $"{booking.Member?.FullName} marked as attended."));
        }, cancellationToken);

    public Task<Result<YogaAttendanceRosterResponse>> SubmitAsync(
        int sessionId, string userId, bool isStaff, SubmitYogaAttendanceRequest request, CancellationToken cancellationToken = default) =>
        WriteAsync(sessionId, userId, isStaff, async (session, now) =>
        {
            var roster = Roster(session);
            if (request.Marks.Any(m => roster.All(b => b.Id != m.BookingId)))
                return Result<YogaAttendanceRosterResponse>.Failure("A marked booking is not on this session's roster.", "NOT_ON_ROSTER", 400);
            if (roster.Any(b => request.Marks.All(m => m.BookingId != b.Id) && RowOf(session, b) is null))   // GBR-87
                return Result<YogaAttendanceRosterResponse>.Failure(
                    "Mark every booked member as Attended or No-show.", "ROSTER_INCOMPLETE", 400);

            foreach (var mark in request.Marks)
                Mark(session, roster.First(b => b.Id == mark.BookingId),
                    mark.Attended ? YogaAttendanceStatus.Present : YogaAttendanceStatus.NoShow, now);
            session.AttendanceSubmittedAt = now;   // closes the session; late when over 30 minutes after start (GBR-86)
            _repository.Add(AuditedWriter.Audit(userId, AuditActionType.Update, nameof(YogaAttendance), null,
                newValue: $"Session={session.Id}; Marked={request.Marks.Count}; Late={SubmittedLate(session)}"));
            await _repository.SaveChangesAsync(cancellationToken);
            return Result<YogaAttendanceRosterResponse>.Success(ToResponse(session, "Attendance updated successfully."));   // MSG-03
        }, cancellationToken);

    /// <summary>Runs a roster change under the session lock, after the access, closed and timing checks.</summary>
    private async Task<Result<YogaAttendanceRosterResponse>> WriteAsync(
        int sessionId, string userId, bool isStaff,
        Func<YogaClassSchedule, DateTime, Task<Result<YogaAttendanceRosterResponse>>> work, CancellationToken cancellationToken)
    {
        Result<YogaAttendanceRosterResponse> result = null!;
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            await _repository.LockSessionAsync(sessionId, cancellationToken);
            var session = await _repository.GetSessionAsync(sessionId, cancellationToken);
            var now = _clock.GetUtcNow().UtcDateTime;
            result = !CanOpen(session, userId, isStaff) ? NotFound()
                : session!.AttendanceSubmittedAt is not null
                    ? Result<YogaAttendanceRosterResponse>.Failure("Attendance for this session is already submitted.", "ATTENDANCE_CLOSED", 409)
                : now < Start(session) - OpensBefore
                    ? Result<YogaAttendanceRosterResponse>.Failure("Attendance opens 30 minutes before the session starts.", "SESSION_NOT_STARTED", 409)
                : await work(session, now);
        }, cancellationToken);
        return result;
    }

    private static bool CanOpen(YogaClassSchedule? session, string userId, bool isStaff) =>
        session is not null && session.ScheduleStatus != YogaScheduleStatus.Cancelled &&
        (isStaff || (session.InstructorUserId ?? session.YogaClass.InstructorUserId) == userId);

    /// <summary>Bookings held when the session started; a later enrolment is not on an earlier session's roster.</summary>
    private static List<YogaBooking> Roster(YogaClassSchedule session)
    {
        var start = Start(session);
        return session.YogaClass.Bookings
            .Where(b => b.BookedAt <= start &&
                        (b.CancelledAt is null ? b.BookingStatus == YogaBookingStatus.Confirmed : b.CancelledAt > start))
            .OrderBy(b => b.MatNumber).ToList();
    }

    private static YogaAttendance? RowOf(YogaClassSchedule session, YogaBooking booking) =>
        session.Attendances.FirstOrDefault(a => a.YogaBookingId == booking.Id);

    private static void Mark(YogaClassSchedule session, YogaBooking booking, YogaAttendanceStatus status, DateTime now)
    {
        var row = RowOf(session, booking);
        if (row is null)
            session.Attendances.Add(row = new YogaAttendance
            {
                YogaClassScheduleId = session.Id,
                YogaBookingId = booking.Id,
                MemberUserId = booking.MemberUserId,
                InstructorUserId = session.InstructorUserId ?? session.YogaClass.InstructorUserId
            });
        row.AttendanceStatus = status;
        row.RecordedAt = now;
    }

    private static YogaAttendanceRosterResponse ToResponse(YogaClassSchedule session, string? message = null) => new(
        session.Id, session.YogaClass.ClassName, session.ScheduledDate, session.StartTime,
        session.AttendanceSubmittedAt, SubmittedLate(session),
        Roster(session).Select(b => new YogaAttendanceEntry(b.Id, b.MemberUserId, b.Member?.FullName ?? string.Empty, b.MatNumber,
            RowOf(session, b)?.AttendanceStatus switch
            {
                YogaAttendanceStatus.Present => "Attended",
                YogaAttendanceStatus.NoShow or YogaAttendanceStatus.Absent => "No-show",
                _ => null
            })).ToList(),
        message);

    private static DateTime Start(YogaClassSchedule s) => YogaTime.ToUtc(s.ScheduledDate, s.StartTime);

    private static Result<YogaAttendanceRosterResponse> NotFound() =>
        Result<YogaAttendanceRosterResponse>.Failure("Yoga session not found.", "YOGA_SESSION_NOT_FOUND", 404);
}
