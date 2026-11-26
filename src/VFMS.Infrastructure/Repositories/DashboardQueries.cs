using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Dashboards;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class DashboardQueries(VfmsDbContext db, IMembershipPurchaseRepository orders)
{
    private static DateTime MidnightUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue).AddHours(-7);
    private static DateOnly GymDay(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(7));
    private static ChartPoint[] Series(DateOnly start, DateOnly end, IReadOnlyDictionary<DateTime, decimal> values) =>
        Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(offset => start.AddDays(offset))
            .Select(date => new ChartPoint(date, values.GetValueOrDefault(date.ToDateTime(TimeOnly.MinValue)))).ToArray();

    public Task<int?> GymAsync(string user, CancellationToken ct) =>
        db.ApplicationUsers.Where(u => u.UserId == user).Select(u => (int?)u.GymId).SingleOrDefaultAsync(ct);

    public async Task<DashboardInbox> InboxAsync(string user, CancellationToken ct)
    {
        var rows = db.Notifications.Where(n => n.RecipientUserId == user);
        return new(await rows.CountAsync(n => !n.IsRead, ct), await rows.OrderByDescending(n => n.NotifiedAt)
            .ThenByDescending(n => n.Id).Take(8).Select(n => new DashboardNotice(n.Title, n.Message, n.NotifiedAt, n.IsRead)).ToListAsync(ct));
    }

    public async Task<AdminOverview> AdminAsync(DateTime now, int days, CancellationToken ct)
    {
        var today = GymDay(now);
        var first = today.AddDays(1 - days);
        var since = MidnightUtc(first);
        var users = db.ApplicationUsers;
        // Assign each account to exactly one primary role so multi-role accounts do not inflate the chart.
        var roleCounts = new List<CategoryCount>();
        string[] roles = ["Other", "Admin", "Manager", "Staff", "PT", "YogaInstructor", "Member", "Customer", "Other"];
        var assignments = await db.ApplicationUserRoles.Where(r => !r.User.IsDeleted)
            .GroupBy(r => r.UserId).Select(g => g.Min(r => r.Role.RoleName == "Admin" ? 1 :
                r.Role.RoleName == "Manager" ? 2 : r.Role.RoleName == "Staff" ? 3 : r.Role.RoleName == "PT" ? 4 :
                r.Role.RoleName == "YogaInstructor" ? 5 : r.Role.RoleName == "Member" ? 6 :
                r.Role.RoleName == "Customer" ? 7 : 8))
            .GroupBy(rank => rank).Select(g => new { Rank = g.Key, Count = g.Count() }).ToListAsync(ct);
        var total = await users.CountAsync(ct);
        foreach (var group in assignments)
            roleCounts.Add(new(roles[group.Rank], group.Count));
        var unassigned = total - assignments.Sum(g => g.Count);
        if (unassigned > 0) roleCounts.Add(new("Unassigned", unassigned));
        var logs = db.AuditLogs.Where(l => l.OccurredAt >= since && l.OccurredAt <= now);
        var daily = await logs.GroupBy(l => l.OccurredAt.AddHours(7).Date)
            .Select(g => new { Date = g.Key, Value = g.Count() }).ToDictionaryAsync(x => x.Date, x => (decimal)x.Value, ct);
        var recent = await logs.OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id).Take(5)
            .Select(l => new AuditActivity(l.OccurredAt, l.ActorUser == null ? null : l.ActorUser.FullName,
                l.ActionType.ToString(), l.AuditEntity, l.EntityId)).ToListAsync(ct);
        return new(today, total, await users.CountAsync(u => u.AccountStatus == AccountStatus.Active, ct),
            await users.CountAsync(u => u.CreatedAt >= since && u.CreatedAt <= now, ct),
            await users.CountAsync(u => u.AccountStatus == AccountStatus.Locked, ct),
            await db.TrainerApplications.CountAsync(a => a.ApplicationStatus == TrainerApplicationStatus.Pending, ct),
            await db.PurchaseProposals.CountAsync(p => p.ProposalStatus == PurchaseProposalStatus.Submitted, ct),
            roleCounts.OrderByDescending(r => r.Count).ToArray(), Series(first, today, daily), recent);
    }
}
