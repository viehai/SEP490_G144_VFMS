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
