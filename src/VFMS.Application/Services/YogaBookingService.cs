using System.Globalization;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// UC-38 Book Yoga Class &amp; Reserve Mat. Booking is per class (ERD): one confirmed booking holds a mat for every
/// session of the class. Seat count, mat and duplicate checks run under a per-class lock.
/// </summary>
public sealed class YogaBookingService : IYogaBookingService
{
    public static readonly TimeSpan NoShowWindow = TimeSpan.FromDays(30);   // GBR-84
    public static readonly TimeSpan Suspension = TimeSpan.FromDays(7);
    public const int NoShowLimit = 3;
    public static readonly TimeSpan LateCancellationCutoff = TimeSpan.FromHours(2);   // GBR-83

    private readonly IYogaBookingRepository _repository;
    private readonly TimeProvider _clock;

    public YogaBookingService(IYogaBookingRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<YogaMatMapResponse>> GetMatMapAsync(int classId, CancellationToken cancellationToken = default)
    {
        var yogaClass = await _repository.GetClassAsync(classId, cancellationToken);
        if (yogaClass is null || yogaClass.ClassStatus is not (YogaClassStatus.Open or YogaClassStatus.Full))
            return NotFound<YogaMatMapResponse>();
        var taken = Confirmed(yogaClass).Where(b => b.MatNumber is not null).Select(b => b.MatNumber!.Value).Order().ToList();
        return Result<YogaMatMapResponse>.Success(new YogaMatMapResponse(classId, yogaClass.MaxParticipants, taken));
    }

    public async Task<Result<YogaBookingResponse>> BookAsync(
        string memberUserId, YogaBookingRequest request, CancellationToken cancellationToken = default)
    {
        Result<YogaBookingResponse> result = null!;
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            await _repository.LockClassAsync(request.YogaClassId, cancellationToken);
            result = await BookLockedAsync(memberUserId, request, cancellationToken);
        }, cancellationToken);
        return result;
    }

    private async Task<Result<YogaBookingResponse>> BookLockedAsync(
        string memberUserId, YogaBookingRequest request, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var yogaClass = await _repository.GetClassAsync(request.YogaClassId, cancellationToken);
        if (yogaClass is null || yogaClass.ClassStatus is YogaClassStatus.Draft)
            return NotFound<YogaBookingResponse>();

        // Precondition: an active package that includes yoga, of this class type when the package names one (MSG-31).
        var memberships = (await _repository.GetMembershipsAsync(memberUserId, cancellationToken))
            .Where(m => m.PackageComponent.YogaClassTypeId is null || m.PackageComponent.YogaClassTypeId == yogaClass.YogaClassTypeId);
        if (MembershipRules.Usable(memberships, YogaTime.Today(_clock), ComponentType.YOGA) is null)
            return Result<YogaBookingResponse>.Failure(
                "An active membership package is required to book Yoga classes or Personal Trainer sessions.", "MEMBERSHIP_REQUIRED", 403);

        if (SuspendedUntil(await _repository.GetNoShowTimesAsync(memberUserId, now - NoShowWindow - Suspension, cancellationToken), now) is { } until)
            return Result<YogaBookingResponse>.Failure(
                string.Create(CultureInfo.InvariantCulture,
                    $"You cannot book yoga classes until {until + YogaTime.GymOffset:dd/MM/yyyy HH:mm} after {NoShowLimit} no-shows within 30 days."),
                "YOGA_BOOKING_SUSPENDED", 403);   // GBR-84

        if (yogaClass.ClassStatus is not (YogaClassStatus.Open or YogaClassStatus.Full) || now >= yogaClass.RegistrationDeadline)
            return Result<YogaBookingResponse>.Failure("Registration for this class is closed.", "REGISTRATION_CLOSED", 409);   // GBR-78

        var confirmed = Confirmed(yogaClass).ToList();
        if (confirmed.Any(b => b.MemberUserId == memberUserId))   // GBR-79
            return Result<YogaBookingResponse>.Failure(
                "You already have another active class or training session scheduled during this time slot.", "ALREADY_BOOKED", 409);   // MSG-32

        var waitlist = await _repository.GetWaitlistAsync(yogaClass.Id, cancellationToken);
        var myWaitlistEntry = waitlist.FirstOrDefault(w => w.MemberUserId == memberUserId);

        if (confirmed.Count >= yogaClass.MaxParticipants)
            return myWaitlistEntry is not null
                ? Result<YogaBookingResponse>.Failure(
                    $"The class is still full. You are number {myWaitlistEntry.Position} on the waitlist.", "ALREADY_WAITLISTED", 409)
                : await JoinWaitlistAsync(memberUserId, yogaClass, waitlist, now, cancellationToken);

        var taken = confirmed.Where(b => b.MatNumber is not null).Select(b => b.MatNumber!.Value).ToHashSet();
        var mat = request.MatNumber ?? Enumerable.Range(1, yogaClass.MaxParticipants).FirstOrDefault(n => !taken.Contains(n));
        if (mat > yogaClass.MaxParticipants)
            return Result<YogaBookingResponse>.Failure(
                $"Choose a mat from 1 to {yogaClass.MaxParticipants}.", "INVALID_MAT", 400);
        if (mat == 0 || taken.Contains(mat))
            return Result<YogaBookingResponse>.Failure(
                "The selected mat position has just been reserved by another member. Please choose another mat.", "MAT_TAKEN", 409);   // MSG-33

        var booking = new YogaBooking
        {
            YogaClassId = yogaClass.Id,
            MemberUserId = memberUserId,
            MatNumber = mat,
            BookedAt = now,
            BookingStatus = YogaBookingStatus.Confirmed
        };
        _repository.Add(booking);
        if (myWaitlistEntry is not null)
            myWaitlistEntry.WaitlistStatus = YogaWaitlistStatus.Promoted;   // GBR-82: the notified member took the seat
        if (confirmed.Count + 1 >= yogaClass.MaxParticipants)
            yogaClass.ClassStatus = YogaClassStatus.Full;   // GBR-80
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<YogaBookingResponse>.Success(new YogaBookingResponse(
            booking.Id, yogaClass.Id, yogaClass.ClassName, "Confirmed", mat, null, "Yoga booking created successfully."), 201);   // MSG-04
    }

    public async Task<Result<MyYogaBookingsResponse>> GetMyBookingsAsync(string memberUserId, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var bookings = await _repository.GetMemberBookingsAsync(memberUserId, cancellationToken);

        var upcoming = bookings
            .Where(b => b.BookingStatus == YogaBookingStatus.Confirmed)
            .Select(b => (Booking: b, Next: NextSession(b.YogaClass, now)))
            .Where(x => x.Next is not null)
            .OrderBy(x => x.Next!.ScheduledDate).ThenBy(x => x.Next!.StartTime)
            .Select(x => new MyYogaBookingResponse(x.Booking.Id, x.Booking.YogaClassId, x.Booking.YogaClass.ClassName,
                x.Next!.ScheduledDate, x.Next.StartTime, x.Next.EndTime, x.Next.YogaStudio.StudioName, x.Booking.MatNumber, "Confirmed"))
            .ToList();

        // Past tab: sessions that started while the member held the booking.
        var past = bookings
            .SelectMany(b => b.YogaClass.Schedules
                .Where(s => s.ScheduleStatus != YogaScheduleStatus.Cancelled)
                .Select(s => (Booking: b, Session: s, Start: YogaTime.ToUtc(s.ScheduledDate, s.StartTime)))
                .Where(x => x.Start <= now && x.Start >= x.Booking.BookedAt &&
                            (x.Booking.CancelledAt is null || x.Start <= x.Booking.CancelledAt ||
                             x.Booking.Attendances.Any(a => a.YogaClassScheduleId == x.Session.Id))))
            .OrderByDescending(x => x.Start)
            .Select(x => new MyYogaPastSessionResponse(x.Session.Id, x.Booking.YogaClass.ClassName, x.Session.ScheduledDate,
                x.Session.StartTime, x.Session.YogaStudio.StudioName,
                ResultOf(x.Booking.Attendances.FirstOrDefault(a => a.YogaClassScheduleId == x.Session.Id))))
            .ToList();

        var waitlist = (await _repository.GetMemberWaitlistAsync(memberUserId, cancellationToken))
            .Select(w => new MyYogaWaitlistResponse(w.YogaClassSchedule.YogaClassId, w.YogaClassSchedule.YogaClass.ClassName,
                w.Position, w.NotifiedAt is not null))
            .ToList();
        // An empty result is still 200; the screen shows MSG-01 "No matching records found."
        return Result<MyYogaBookingsResponse>.Success(new MyYogaBookingsResponse(upcoming, past, waitlist));
    }

    private static string ResultOf(YogaAttendance? attendance) => attendance?.AttendanceStatus switch
    {
        YogaAttendanceStatus.Present => "Attended",
        YogaAttendanceStatus.NoShow or YogaAttendanceStatus.Absent => "No-show",
        _ => "Not recorded"
    };

    public async Task<Result<YogaBookingResponse>> CancelAsync(
        string memberUserId, int bookingId, CancelYogaBookingRequest request, CancellationToken cancellationToken = default)
    {
        var booking = await _repository.GetBookingAsync(bookingId, cancellationToken);
        if (booking is null || booking.MemberUserId != memberUserId || booking.BookingStatus != YogaBookingStatus.Confirmed)
            return Result<YogaBookingResponse>.Failure("Yoga booking not found.", "YOGA_BOOKING_NOT_FOUND", 404);

        Result<YogaBookingResponse> result = null!;
        await _repository.ExecuteInTransactionAsync(async () =>
        {
            await _repository.LockClassAsync(booking.YogaClassId, cancellationToken);
            result = await CancelLockedAsync(booking, request, cancellationToken);
        }, cancellationToken);
        return result;
    }

    private async Task<Result<YogaBookingResponse>> CancelLockedAsync(
        YogaBooking booking, CancelYogaBookingRequest request, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var yogaClass = (await _repository.GetClassAsync(booking.YogaClassId, cancellationToken))!;
        var next = NextSession(yogaClass, now);
        if (next is null)
            return Result<YogaBookingResponse>.Failure("This class has no upcoming session to cancel.", "NO_UPCOMING_SESSION", 409);

        // GBR-83: free up to 2 hours before the next session; later only after the member accepts MSG-34.
        var late = YogaTime.ToUtc(next.ScheduledDate, next.StartTime) - now < LateCancellationCutoff;
        if (late && !request.ConfirmLateCancellation)
            return Result<YogaBookingResponse>.Failure(
                "Class cancellations must be submitted at least 2 hours prior to start time. Late cancellation incurs a no-show penalty.",
                "LATE_CANCELLATION_CONFIRM", 409);   // MSG-34

        booking.BookingStatus = YogaBookingStatus.Cancelled;
        booking.CancelledAt = now;
        // A late cancellation counts as a no-show of that session (GBR-83, GBR-84), unless attendance (e.g. a QR scan,
        // UC-42) already marked this booking; the session lock keeps that check and the attendance writes in order.
        await _repository.LockSessionAsync(next.Id, cancellationToken);
        if (late && !(await _repository.GetSessionAsync(next.Id, cancellationToken))!.Attendances.Any(a => a.YogaBookingId == booking.Id))
            _repository.Add(new YogaAttendance
            {
                YogaClassScheduleId = next.Id,
                YogaBookingId = booking.Id,
                MemberUserId = booking.MemberUserId,
                InstructorUserId = next.InstructorUserId ?? yogaClass.InstructorUserId,
                AttendanceStatus = YogaAttendanceStatus.NoShow,
                RecordedAt = now
            });
        if (yogaClass.ClassStatus == YogaClassStatus.Full)
            yogaClass.ClassStatus = YogaClassStatus.Open;   // the seat is free again

        // GBR-82: offer the seat to the first waiting member.
        // shortcut: the seat is not held for them, anyone may book it first; hold it if members complain.
        if ((await _repository.GetWaitlistAsync(yogaClass.Id, cancellationToken)).FirstOrDefault(w => w.NotifiedAt is null) is { } first)
        {
            first.NotifiedAt = now;
            var notice = AuditedWriter.Notify(first.MemberUserId, null, "A yoga seat is free",
                $"A seat opened in {yogaClass.ClassName}. Book it now to confirm your place.", nameof(YogaClass),
                NotificationType.YogaBookingConfirmed);
            notice.EntityId = yogaClass.Id;
            _repository.Add(notice);
        }
        await _repository.SaveChangesAsync(cancellationToken);

        var message = "Yoga booking updated successfully.";   // MSG-03
        if (late && SuspendedUntil(await _repository.GetNoShowTimesAsync(booking.MemberUserId, now - NoShowWindow - Suspension, cancellationToken), now) is { } until)
            message += string.Create(CultureInfo.InvariantCulture,
                $" This is your third no-show within 30 days, so yoga booking is suspended until {until + YogaTime.GymOffset:dd/MM/yyyy HH:mm}.");
        return Result<YogaBookingResponse>.Success(new YogaBookingResponse(
            booking.Id, yogaClass.Id, yogaClass.ClassName, "Cancelled", booking.MatNumber, null, message));
    }

    /// <summary>
    /// GBR-80/82: a full class queues the member without payment. The ERD keys the waitlist to a session, so the entry
    /// is attached to the class's next upcoming session; queue order is per class.
    /// </summary>
    private async Task<Result<YogaBookingResponse>> JoinWaitlistAsync(
        string memberUserId, YogaClass yogaClass, IReadOnlyList<YogaWaitlist> waitlist, DateTime now, CancellationToken cancellationToken)
    {
        var next = NextSession(yogaClass, now);
        if (next is null)
            return Result<YogaBookingResponse>.Failure("Registration for this class is closed.", "REGISTRATION_CLOSED", 409);
        var entry = new YogaWaitlist
        {
            YogaClassScheduleId = next.Id,
            YogaClassTypeId = yogaClass.YogaClassTypeId,
            MemberUserId = memberUserId,
            Position = waitlist.Select(w => w.Position).DefaultIfEmpty(0).Max() + 1,
            JoinedAt = now
        };
        _repository.Add(entry);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result<YogaBookingResponse>.Success(new YogaBookingResponse(
            null, yogaClass.Id, yogaClass.ClassName, "Waitlisted", null, entry.Position,
            $"The class is full. You are number {entry.Position} on the waitlist; we will notify you when a seat opens."), 202);
    }

    /// <summary>GBR-84: end of the booking ban if some no-show in the last 7 days was the 3rd within 30 days, else null.</summary>
    public static DateTime? SuspendedUntil(IReadOnlyList<DateTime> noShows, DateTime now) =>
        noShows
            .Where(x => x + Suspension > now && noShows.Count(y => y <= x && y > x - NoShowWindow) >= NoShowLimit)
            .Select(x => (DateTime?)(x + Suspension))
            .Max();

    internal static YogaClassSchedule? NextSession(YogaClass yogaClass, DateTime now) =>
        yogaClass.Schedules
            .Where(s => s.ScheduleStatus == YogaScheduleStatus.Scheduled && YogaTime.ToUtc(s.ScheduledDate, s.StartTime) > now)
            .MinBy(s => YogaTime.ToUtc(s.ScheduledDate, s.StartTime));

    private static IEnumerable<YogaBooking> Confirmed(YogaClass c) => c.Bookings.Where(b => b.BookingStatus == YogaBookingStatus.Confirmed);

    private static Result<T> NotFound<T>() => Result<T>.Failure("Yoga class not found.", "YOGA_CLASS_NOT_FOUND", 404);
}
