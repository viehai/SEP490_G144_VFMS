using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Common;
using VFMS.Contracts.Dashboards;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class DashboardQueries
{
    public async Task<PagedResponse<DashboardNotice>> NotificationHistoryAsync(string user, DateTime since, int page, CancellationToken ct)
    {
        var rows = db.Notifications.Where(n => n.RecipientUserId == user && n.NotifiedAt >= since);
        return new(page, 10, await rows.CountAsync(ct), await rows.OrderByDescending(n => n.NotifiedAt)
            .ThenByDescending(n => n.Id).Skip((page - 1) * 10).Take(10)
            .Select(n => new DashboardNotice(n.Title, n.Message, n.NotifiedAt, n.IsRead, n.Id)).ToListAsync(ct));
    }

    public async Task<bool> MarkNotificationReadAsync(string user, int id, DateTime now, CancellationToken ct)
    {
        var row = db.Notifications.Where(n => n.Id == id && n.RecipientUserId == user);
        if (!await row.AnyAsync(ct)) return false;
        await row.Where(n => !n.IsRead).ExecuteUpdateAsync(update => update
            .SetProperty(n => n.IsRead, true).SetProperty(n => n.UpdatedAt, now), ct);
        return true;
    }

    public Task<int> MarkAllNotificationsReadAsync(string user, DateTime now, CancellationToken ct) =>
        db.Notifications.Where(n => n.RecipientUserId == user && !n.IsRead).ExecuteUpdateAsync(update => update
            .SetProperty(n => n.IsRead, true).SetProperty(n => n.UpdatedAt, now), ct);
}
