using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class YogaBookingRepository : AuditedRepositoryBase, IYogaBookingRepository
{
    public YogaBookingRepository(VfmsDbContext context) : base(context) { }

    public Task<YogaClass?> GetClassAsync(int classId, CancellationToken cancellationToken = default) =>
        Context.YogaClasses
            .Include(c => c.Schedules).ThenInclude(s => s.YogaStudio)
            .Include(c => c.Bookings)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);

    public Task LockClassAsync(int classId, CancellationToken cancellationToken = default) =>
        AppLockAsync($"yoga-class-{classId}", "This class is busy. Please try again.", cancellationToken);

    public async Task<IReadOnlyList<MemberMembership>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default) =>
        await Context.MemberMemberships.AsNoTracking().Include(m => m.PackageComponent)
            .Where(m => m.MemberUserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DateTime>> GetNoShowTimesAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await Context.YogaAttendances
            .Where(a => a.MemberUserId == userId && a.AttendanceStatus == YogaAttendanceStatus.NoShow && a.RecordedAt >= sinceUtc)
            .Select(a => a.RecordedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<YogaWaitlist>> GetWaitlistAsync(int classId, CancellationToken cancellationToken = default) =>
        await Context.YogaWaitlists
            .Where(w => w.YogaClassSchedule.YogaClassId == classId && w.WaitlistStatus == YogaWaitlistStatus.Waiting)
            .OrderBy(w => w.Position)
            .ToListAsync(cancellationToken);

    public Task<YogaBooking?> GetBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
        Context.YogaBookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
}
