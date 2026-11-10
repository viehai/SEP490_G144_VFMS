using VFMS.Contracts.Common;
using VFMS.Contracts.Yoga;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IYogaBookingRepository : IAuditedRepository
{
    /// <summary>Class with sessions (studio) and bookings loaded, tracked.</summary>
    Task<YogaClass?> GetClassAsync(int classId, CancellationToken cancellationToken = default);
    /// <summary>Exclusive lock on one class's seats until the current transaction ends (last seat, mats, duplicates).</summary>
    Task LockClassAsync(int classId, CancellationToken cancellationToken = default);
    /// <summary>The member's memberships with their package component (no tracking).</summary>
    Task<IReadOnlyList<MemberMembership>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>When the member's no-shows (incl. late cancellations) were recorded, since the given time (GBR-84).</summary>
    Task<IReadOnlyList<DateTime>> GetNoShowTimesAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default);
    /// <summary>Waiting members of a class in queue order (tracked).</summary>
    Task<IReadOnlyList<YogaWaitlist>> GetWaitlistAsync(int classId, CancellationToken cancellationToken = default);
    Task<YogaBooking?> GetBookingAsync(int bookingId, CancellationToken cancellationToken = default);
    /// <summary>All the member's bookings with class, sessions (studio) and attendances; no tracking.</summary>
    Task<IReadOnlyList<YogaBooking>> GetMemberBookingsAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>The member's Waiting entries with session and class; no tracking.</summary>
    Task<IReadOnlyList<YogaWaitlist>> GetMemberWaitlistAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>UC-38 Book Yoga Class &amp; Reserve Mat.</summary>
public interface IYogaBookingService
{
    Task<Result<YogaMatMapResponse>> GetMatMapAsync(int classId, CancellationToken cancellationToken = default);
    Task<Result<YogaBookingResponse>> BookAsync(string memberUserId, YogaBookingRequest request, CancellationToken cancellationToken = default);
    /// <summary>UC-40 View My Yoga Bookings.</summary>
    Task<Result<MyYogaBookingsResponse>> GetMyBookingsAsync(string memberUserId, CancellationToken cancellationToken = default);
    /// <summary>UC-39 Cancel Yoga Booking.</summary>
    Task<Result<YogaBookingResponse>> CancelAsync(
        string memberUserId, int bookingId, CancelYogaBookingRequest request, CancellationToken cancellationToken = default);
}
