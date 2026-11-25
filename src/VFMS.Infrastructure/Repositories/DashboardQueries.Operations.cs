using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Dashboards;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class DashboardQueries
{
    private static StaffPackageOrderFilter OrderFilter(OrderStatus? status = null, PaymentMethod? method = null) =>
        new(null, status, null, method, null, false, false, null, null, 1, 5, NewestFirst: true);

    private static DashboardOrder PackageRow(StaffPackageOrderResponse row) => new(row.OrderNumber, row.PurchaserName,
        string.Join(", ", row.Items.Select(i => i.Name)), row.Status, row.AmountDue, row.Method, row.CreatedAt.UtcDateTime);

    private IQueryable<PTSession> TeachingSessions => db.PTSessions.Where(s =>
        s.SessionStatus != PTSessionStatus.Cancelled && s.SessionStatus != PTSessionStatus.CheckoutHold &&
        s.SessionStatus != PTSessionStatus.MakeUpRequested && s.SessionStatus != PTSessionStatus.MakeUpPending &&
        s.SessionStatus != PTSessionStatus.FreezeHold);

    public async Task<ManagerOverview> ManagerAsync(int gym, DateTime now, DateOnly first, DateOnly last, CancellationToken ct)
    {
        var today = GymDay(now);
        var yesterday = today.AddDays(-1);
        var revenue = await DailyRevenue(gym, first, last, ct);
        var current = first <= yesterday && last == today ? revenue : await DailyRevenue(gym, yesterday, today, ct);
        var soon = today.AddDays(7);
        var categories = await db.MemberMemberships.Where(m => m.Member.GymId == gym && m.MembershipStatus != MembershipStatus.Cancelled)
            .GroupBy(m => m.MemberUserId).Select(g => g.Min(m =>
                m.MembershipStatus == MembershipStatus.Active && m.StartDate <= today && m.EndDate > soon ? 1 :
                m.MembershipStatus == MembershipStatus.Active && m.StartDate <= today && m.EndDate >= today ? 2 :
                m.MembershipStatus == MembershipStatus.Frozen ? 3 :
                m.MembershipStatus == MembershipStatus.Scheduled || m.MembershipStatus == MembershipStatus.AwaitingActivation ? 4 :
                m.MembershipStatus == MembershipStatus.Suspended ? 5 : 6))
            .GroupBy(rank => rank).Select(g => new { Rank = g.Key, Count = g.Count() }).ToListAsync(ct);
        string[] labels = ["", "Active", "Expiring", "Frozen", "Scheduled", "Suspended", "Expired"];
        var packages = await orders.ListStaffOrdersAsync(gym, OrderFilter(), now, ct);
        var pending = await orders.ListStaffOrdersAsync(gym, OrderFilter(OrderStatus.Pending), now, ct);
        var counter = await orders.ListStaffOrdersAsync(gym, OrderFilter(OrderStatus.Pending, PaymentMethod.Cash), now, ct);
        var products = await db.SupplementOrders.Where(o => o.Customer.GymId == gym).OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id).Take(5).Select(o => new DashboardOrder(o.OrderNumber, o.Customer.FullName,
                "Sản phẩm bổ sung", o.OrderStatus == SupplementOrderStatus.Pending ? "SupplementPending" : o.OrderStatus.ToString(),
                o.TotalAmount, null, o.CreatedAt)).ToListAsync(ct);
        var recent = packages.Items.Select(PackageRow).Concat(products).OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Number, StringComparer.Ordinal).Take(5).ToArray();
        return new(today, current.GetValueOrDefault(today.ToDateTime(TimeOnly.MinValue)),
            current.GetValueOrDefault(yesterday.ToDateTime(TimeOnly.MinValue)),
            categories.Where(g => g.Rank <= 2).Sum(g => g.Count), pending.TotalCount,
            await TeachingSessions.CountAsync(s => s.PTUser.GymId == gym && s.SessionDate == today, ct),
            Series(first, last, revenue), categories.OrderBy(g => g.Rank).Select(g => new CategoryCount(labels[g.Rank], g.Count)).ToArray(),
            recent, counter.Items.Select(PackageRow).ToArray());
    }

    // Same paid sources and exclusions as the business report, scoped to the manager's gym and local calendar days.
    private async Task<Dictionary<DateTime, decimal>> DailyRevenue(int gym, DateOnly first, DateOnly last, CancellationToken ct)
    {
        var start = MidnightUtc(first);
        var end = MidnightUtc(last.AddDays(1));
        var payments = db.PaymentTransactions.Where(p => p.Order.Purchaser.GymId == gym && p.PaymentStatus == PaymentStatus.Paid &&
            p.Order.OrderStatus == OrderStatus.Paid && p.SourcePaymentTransactionId == null && p.LockerRentalContractId == null &&
            p.PaidAt >= start && p.PaidAt < end).Select(p => new { Day = p.PaidAt!.Value.AddHours(7).Date, Amount = p.AmountReceived });
        var supplements = db.SupplementOrders.Where(o => o.Customer.GymId == gym && o.OrderStatus == SupplementOrderStatus.Completed &&
            o.CompletedAt >= start && o.CompletedAt < end).Select(o => new { Day = o.CompletedAt!.Value.AddHours(7).Date, Amount = o.TotalAmount });
        // Sum fixed-point minor units to keep decimal precision on both database providers.
        return await payments.Concat(supplements).GroupBy(p => p.Day)
            .Select(g => new { Day = g.Key, MinorUnits = g.Sum(p => (long)(p.Amount * 100m)) })
            .ToDictionaryAsync(p => p.Day, p => p.MinorUnits / 100m, ct);
    }

    public async Task<StaffOverview> StaffAsync(int gym, DateTime now, CancellationToken ct)
    {
        var today = GymDay(now);
        var start = MidnightUtc(today);
        var yesterday = start.AddDays(-1);
        var visits = db.CheckIns.Where(c => c.Member.GymId == gym);
        var todaysVisits = visits.Where(c => c.CheckinTime >= start && c.CheckinTime <= now);
        var arrivals = await todaysVisits.OrderByDescending(c => c.CheckinTime).ThenByDescending(c => c.Id).Take(5)
            .Select(c => new GymVisit(c.Member.FullName, c.CheckinTime, c.CheckoutTime != null)).ToListAsync(ct);
        var pending = await orders.ListStaffOrdersAsync(gym, OrderFilter(OrderStatus.Pending), now, ct);
        // Payment grants the member role, including when the purchased package starts later.
        var newMembers = await db.MemberMemberships.Where(m => m.Member.GymId == gym && m.OrderItem.Order.OrderStatus == OrderStatus.Paid)
            .GroupBy(m => m.MemberUserId).Select(g => g.Min(m => m.OrderItem.Order.CompletedAt ?? m.CreatedAt))
            .CountAsync(first => first >= start && first <= now, ct);
        var schedule = await YogaSlots(YogaOn(today).Where(s => s.YogaStudio.GymId == gym)).ToListAsync(ct);
        return new(today, await todaysVisits.CountAsync(ct), await visits.CountAsync(c => c.CheckinTime >= yesterday && c.CheckinTime < start, ct),
            pending.TotalCount, newMembers, await db.ConsultationConversations.CountAsync(c => c.CustomerUser.GymId == gym && c.Status == "Open", ct),
            arrivals, pending.Items.Select(PackageRow).ToArray(), schedule);
    }
}
