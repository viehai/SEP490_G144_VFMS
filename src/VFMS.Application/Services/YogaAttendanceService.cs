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
            ? Result<YogaAttendanceRosterResponse>.Success(ToResponse(session!, await OnHoldAsync(session!, cancellationToken)))
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
            var onHold = await OnHoldAsync(session, cancellationToken);
            if (onHold.Contains(booking.Id))
                return OnHoldFailure(booking.Member?.FullName);
            Mark(session, booking, YogaAttendanceStatus.Present, now);
            await _repository.SaveChangesAsync(cancellationToken);
            return Result<YogaAttendanceRosterResponse>.Success(ToResponse(session, onHold, $"{booking.Member?.FullName} marked as attended."));
        }, cancellationToken);

    public Task<Result<YogaAttendanceRosterResponse>> SubmitAsync(
        int sessionId, string userId, bool isStaff, SubmitYogaAttendanceRequest request, CancellationToken cancellationToken = default) =>
        WriteAsync(sessionId, userId, isStaff, async (session, now) =>
        {
            var roster = Roster(session);
            if (request.Marks.Any(m => roster.All(b => b.Id != m.BookingId)))
                return Result<YogaAttendanceRosterResponse>.Failure("Có lượt đặt chỗ được điểm danh nhưng không thuộc danh sách buổi học này.", "NOT_ON_ROSTER", 400);
            // TC-YEXP: a booking on hold can't be marked present and is not counted absent, so it needs no mark.
            var onHold = await OnHoldAsync(session, cancellationToken);
            if (request.Marks.FirstOrDefault(m => m.Attended && onHold.Contains(m.BookingId)) is { } held)
                return OnHoldFailure(roster.First(b => b.Id == held.BookingId).Member?.FullName);
            if (roster.Any(b => !onHold.Contains(b.Id) && request.Marks.All(m => m.BookingId != b.Id) && RowOf(session, b) is null))   // GBR-87
                return Result<YogaAttendanceRosterResponse>.Failure(
                    "Hãy điểm danh mọi hội viên đã đặt chỗ: Có mặt hoặc Vắng mặt.", "ROSTER_INCOMPLETE", 400);

            foreach (var mark in request.Marks.Where(m => !onHold.Contains(m.BookingId)))
                Mark(session, roster.First(b => b.Id == mark.BookingId),
                    mark.Attended ? YogaAttendanceStatus.Present : YogaAttendanceStatus.NoShow, now);
            session.AttendanceSubmittedAt = now;   // closes the session; late when over 30 minutes after start (GBR-86)
            _repository.Add(AuditedWriter.Audit(userId, AuditActionType.Update, nameof(YogaAttendance), null,
                newValue: $"Session={session.Id}; Marked={request.Marks.Count}; Late={SubmittedLate(session)}"));
            await _repository.SaveChangesAsync(cancellationToken);
            return Result<YogaAttendanceRosterResponse>.Success(ToResponse(session, onHold, "Cập nhật điểm danh thành công."));   // MSG-03
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
                    ? Result<YogaAttendanceRosterResponse>.Failure("Điểm danh buổi học này đã được nộp.", "ATTENDANCE_CLOSED", 409)
                : now < Start(session) - OpensBefore
                    ? Result<YogaAttendanceRosterResponse>.Failure("Điểm danh mở 30 phút trước khi buổi học bắt đầu.", "SESSION_NOT_STARTED", 409)
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

    /// <summary>Roster bookings whose member has no usable yoga plan covering the class on the session date (TC-YEXP).</summary>
    private async Task<HashSet<int>> OnHoldAsync(YogaClassSchedule session, CancellationToken cancellationToken)
    {
        // shortcut: one membership query per booked member; fine for a class of ~20 mats, batch it if rosters grow.
        var held = new HashSet<int>();
        foreach (var booking in Roster(session))
            if (!MembershipRules.CoversYogaClass(await _repository.GetMembershipsAsync(booking.MemberUserId, cancellationToken),
                    session.YogaClass.DifficultyLevel, session.ScheduledDate))
                held.Add(booking.Id);
        return held;
    }

    private static Result<YogaAttendanceRosterResponse> OnHoldFailure(string? memberName) =>
        Result<YogaAttendanceRosterResponse>.Failure(
            $"Gói yoga của {(string.IsNullOrWhiteSpace(memberName) ? "hội viên" : memberName)} đã hết hạn nên chỗ đang được tạm giữ. " +
            "Hội viên cần mua gói mới để tham gia buổi học.", "YOGA_PLAN_ON_HOLD", 403);

    private static YogaAttendanceRosterResponse ToResponse(YogaClassSchedule session, IReadOnlySet<int> onHold, string? message = null) => new(
        session.Id, session.YogaClass.ClassName, session.ScheduledDate, session.StartTime,
        session.AttendanceSubmittedAt, SubmittedLate(session),
        Roster(session).Select(b => new YogaAttendanceEntry(b.Id, b.MemberUserId, b.Member?.FullName ?? string.Empty, b.MatNumber,
            RowOf(session, b)?.AttendanceStatus switch
            {
                YogaAttendanceStatus.Present => "Attended",
                YogaAttendanceStatus.NoShow or YogaAttendanceStatus.Absent => "No-show",
                _ => null
            }, onHold.Contains(b.Id))).ToList(),
        message);

    private static DateTime Start(YogaClassSchedule s) => YogaTime.ToUtc(s.ScheduledDate, s.StartTime);

    private static Result<YogaAttendanceRosterResponse> NotFound() =>
        Result<YogaAttendanceRosterResponse>.Failure("Không tìm thấy buổi học yoga.", "YOGA_SESSION_NOT_FOUND", 404);
}
