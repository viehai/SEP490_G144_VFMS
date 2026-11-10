using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class YogaRepository : AuditedRepositoryBase, IYogaRepository
{
    public YogaRepository(VfmsDbContext context) : base(context) { }

    public Task<int?> GetDefaultGymIdAsync(CancellationToken cancellationToken = default) =>
        Context.Gyms.OrderBy(g => g.Id).Select(g => (int?)g.Id).FirstOrDefaultAsync(cancellationToken);

    // ── Studios ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<YogaStudio>> GetStudiosAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await Context.YogaStudios.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.FloorNumber).ThenBy(s => s.StudioName)
            .ToListAsync(cancellationToken);

    public Task<YogaStudio?> GetStudioAsync(int id, CancellationToken cancellationToken = default) =>
        Context.YogaStudios.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<bool> StudioNameExistsAsync(int gymId, string name, int? excludingId, CancellationToken cancellationToken = default) =>
        Context.YogaStudios.AnyAsync(s => s.GymId == gymId && s.StudioName.ToLower() == name.ToLower() &&
                                          (excludingId == null || s.Id != excludingId), cancellationToken);

    public async Task<int> GetLargestClassUsingStudioAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
        await Context.YogaClassSchedules
            .Where(s => s.YogaStudioId == studioId && s.ScheduledDate >= fromDate && s.ScheduleStatus == YogaScheduleStatus.Scheduled)
            .Select(s => (int?)s.YogaClass.MaxParticipants)
            .MaxAsync(cancellationToken) ?? 0;

    public Task<bool> StudioHasFutureSessionsAsync(int studioId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
        Context.YogaClassSchedules.AnyAsync(s => s.YogaStudioId == studioId && s.ScheduledDate >= fromDate &&
                                                 s.ScheduleStatus == YogaScheduleStatus.Scheduled, cancellationToken);

    // ── Class types ──────────────────────────────────────────────

    public async Task<IReadOnlyList<YogaClassType>> GetClassTypesAsync(CancellationToken cancellationToken = default) =>
        await Context.YogaClassTypes.AsNoTracking().OrderBy(t => t.TypeName).ToListAsync(cancellationToken);

    public Task<YogaClassType?> GetClassTypeAsync(int id, CancellationToken cancellationToken = default) =>
        Context.YogaClassTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> ClassTypeNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default) =>
        Context.YogaClassTypes.AnyAsync(t => t.TypeName.ToLower() == name.ToLower() && (excludingId == null || t.Id != excludingId),
            cancellationToken);

    // ── Classes ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<YogaClass>> GetClassesAsync(CancellationToken cancellationToken = default) =>
        await Context.YogaClasses.AsNoTracking()
            .Include(c => c.YogaClassType)
            .Include(c => c.Instructor)
            .Include(c => c.Schedules)
            .OrderByDescending(c => c.StartDate).ThenBy(c => c.ClassName)
            .ToListAsync(cancellationToken);

    public Task<YogaClass?> GetClassAsync(int id, CancellationToken cancellationToken = default) =>
        Context.YogaClasses
            .Include(c => c.YogaClassType)
            .Include(c => c.Instructor)
            .Include(c => c.Schedules).ThenInclude(s => s.YogaStudio)
            .Include(c => c.Bookings)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    // ── Sessions ─────────────────────────────────────────────────

    public Task<YogaClassSchedule?> GetSessionAsync(int id, CancellationToken cancellationToken = default) =>
        Context.YogaClassSchedules
            .Include(s => s.YogaClass).ThenInclude(c => c.YogaClassType)
            .Include(s => s.YogaClass).ThenInclude(c => c.Bookings)
            .Include(s => s.YogaStudio)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<YogaClassSchedule>> GetActiveSessionsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await Context.YogaClassSchedules.AsNoTracking()
            .Include(s => s.YogaClass)
            .Where(s => s.ScheduledDate == date && s.ScheduleStatus != YogaScheduleStatus.Cancelled)
            .ToListAsync(cancellationToken);

    public Task LockScheduleDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        AppLockAsync($"yoga-sessions-{date:yyyy-MM-dd}", "The yoga timetable is busy. Please try again.", cancellationToken);

    public async Task<IReadOnlyList<YogaClassSchedule>> GetSessionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await Context.YogaClassSchedules.AsNoTracking()
            .Include(s => s.YogaClass).ThenInclude(c => c.YogaClassType)
            .Include(s => s.YogaClass).ThenInclude(c => c.Bookings)
            .Include(s => s.YogaStudio)
            .Where(s => s.ScheduledDate >= from && s.ScheduledDate <= to)
            .OrderBy(s => s.ScheduledDate).ThenBy(s => s.StartTime)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    // ── Users ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
        await Context.ApplicationUserRoles.AsNoTracking()
            .Where(ur => ur.Role.RoleName == roleName && ur.User.AccountStatus == AccountStatus.Active)
            .Select(ur => ur.User)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, string>();
        return await Context.ApplicationUsers.AsNoTracking()
            .Where(u => ids.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
    }
}
