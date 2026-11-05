using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class GymSettingsRepository : AuditedRepositoryBase, IGymSettingsRepository
{
    public GymSettingsRepository(VfmsDbContext context) : base(context) { }

    public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) =>
        Context.Gyms.OrderBy(g => g.Id).FirstOrDefaultAsync(cancellationToken);
}

public sealed class BusinessReportRepository : AuditedRepositoryBase, IBusinessReportRepository
{
    public BusinessReportRepository(VfmsDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<BusinessReportSnapshot> Items, int TotalCount)> SearchAsync(
        DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.BusinessReportSnapshots.AsNoTracking().AsQueryable();
        if (from.HasValue)
            query = query.Where(r => r.PeriodEnd >= from);
        if (to.HasValue)
            query = query.Where(r => r.PeriodStart <= to);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(r => r.GeneratedByUser)
            .OrderByDescending(r => r.GeneratedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<BusinessReportSnapshot?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Context.BusinessReportSnapshots.AsNoTracking()
            .Include(r => r.GeneratedByUser)
            .Include(r => r.Metrics)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<BusinessReportSnapshot?> FindByPeriodAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
        Context.BusinessReportSnapshots.AsNoTracking()
            .Where(r => r.PeriodStart == start && r.PeriodEnd == end)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Read-only queries over the source tables (GBR-63). Every query runs with no tracking.</summary>
    public async Task<ReportSourceData> GetSourceDataAsync(
        DateOnly start, DateOnly end, DateTime startUtc, DateTime endUtcExclusive, CancellationToken cancellationToken = default)
    {
        // Membership revenue: money received for paid membership orders, by payment time.
        var membershipRevenue = await Context.PaymentTransactions.AsNoTracking()
            .Where(p => p.PaymentStatus == PaymentStatus.Paid && p.Order.OrderStatus == OrderStatus.Paid &&
                        p.PaidAt >= startUtc && p.PaidAt < endUtcExclusive)
            .SumAsync(p => (decimal?)p.AmountReceived, cancellationToken) ?? 0m;

        // Supplement sales: completed (= paid, GBR-59) supplement orders, by completion time.
        var supplementSales = await Context.SupplementOrders.AsNoTracking()
            .Where(o => o.OrderStatus == SupplementOrderStatus.Completed &&
                        o.CompletedAt >= startUtc && o.CompletedAt < endUtcExclusive)
            .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;

        var memberships = Context.MemberMemberships.AsNoTracking().Where(m => m.MembershipStatus != MembershipStatus.Cancelled);

        // New members: people whose first membership starts in the period.
        var newMembers = await memberships
            .GroupBy(m => m.MemberUserId)
            .Select(g => g.Min(m => m.StartDate))
            .CountAsync(first => first >= start && first <= end, cancellationToken);

        // Active memberships: memberships whose validity overlaps the period.
        var activeMemberships = await memberships
            .CountAsync(m => m.StartDate <= end && m.EndDate >= start, cancellationToken);

        var ptCompleted = await Context.PTSessions.AsNoTracking()
            .CountAsync(s => s.SessionStatus == PTSessionStatus.Completed && s.SessionDate >= start && s.SessionDate <= end,
                cancellationToken);

        var yogaAttendance = await Context.YogaAttendances.AsNoTracking()
            .CountAsync(a => a.AttendanceStatus == YogaAttendanceStatus.Present &&
                             a.RecordedAt >= startUtc && a.RecordedAt < endUtcExclusive, cancellationToken);

        // Locker occupancy: lockers with a rental contract covering any part of the period.
        var totalLockers = await Context.Lockers.AsNoTracking().CountAsync(cancellationToken);
        var occupiedLockers = await Context.LockerRentalContracts.AsNoTracking()
            .Where(c => c.StartDate <= end && c.EndDate >= start && (c.CancelledAt == null || c.CancelledAt >= startUtc))
            .Select(c => c.LockerId)
            .Distinct()
            .CountAsync(cancellationToken);

        var equipmentIssues = await Context.EquipmentIssueReports.AsNoTracking()
            .CountAsync(i => i.ReportedAt >= startUtc && i.ReportedAt < endUtcExclusive, cancellationToken);

        return new ReportSourceData(membershipRevenue, supplementSales, newMembers, activeMemberships, ptCompleted,
            yogaAttendance, totalLockers, Math.Min(occupiedLockers, totalLockers), equipmentIssues);
    }
}
