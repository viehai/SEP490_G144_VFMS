using System.Data;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipPurchaseRepository(VfmsDbContext context) : IMembershipPurchaseRepository
{
    public async Task<T> ExecuteInSerializableTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }

    public Task<List<Package>> LoadPlansAsync(int[] ids, CancellationToken ct) =>
        context.Packages.AsNoTracking().Where(plan => ids.Contains(plan.Id))
            .Include(plan => plan.Components).ToListAsync(ct);

    public Task<List<ApplicationUser>> LoadCheckoutUsersAsync(string purchaserId, string[] emails, CancellationToken ct)
    {
        var users = context.ApplicationUsers.AsNoTracking();
        var matches = emails.Length == 0 ? users.Where(user => user.UserId == purchaserId)
            : users.Where(user => user.UserId == purchaserId || emails.Contains(user.Email.ToLower()));
        return matches.Include(user => user.UserRoles).ThenInclude(role => role.Role).ToListAsync(ct);
    }

    public Task<List<ApplicationUser>> LockUsersAsync(string[] userIds, CancellationToken ct)
    {
        // Serialize same-beneficiary fulfillment even when callbacks belong to different orders.
        var users = context.Database.IsSqlServer()
            ? context.ApplicationUsers.FromSqlRaw("SELECT * FROM [ApplicationUsers] WITH (UPDLOCK, HOLDLOCK)")
            : context.ApplicationUsers;
        return users.Where(user => userIds.Contains(user.UserId)).OrderBy(user => user.UserId)
            .Include(user => user.UserRoles).ThenInclude(role => role.Role).ToListAsync(ct);
    }

    public Task<ApplicationRole?> GetMemberRoleAsync(CancellationToken ct) =>
        context.ApplicationRoles.SingleOrDefaultAsync(role => role.RoleName == nameof(UserRole.Member), ct);

    public Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct)
    {
        var orders = forUpdate && context.Database.IsSqlServer()
            ? context.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK) WHERE [OrderNumber] = {orderNumber}")
            : context.Orders;
        var query = orders.Where(order => order.OrderNumber == orderNumber);
        if (purchaserId is not null)
            query = query.Where(order => order.PurchaserUserId == purchaserId);
        if (!forUpdate)
            query = query.AsNoTracking().Include(order => order.Items);
        else
            query = query.Include(order => order.Purchaser)
                .Include(order => order.Items).ThenInclude(item => item.Package).ThenInclude(plan => plan.Components);

        return query.Include(order => order.PaymentTransaction)!.ThenInclude(payment => payment!.Invoice)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
    }

    public Task<bool> SepayReferenceUsedAsync(string reference, CancellationToken ct) =>
        context.PaymentTransactions.IgnoreQueryFilters().AnyAsync(
            payment => payment.GatewayName == "SePay" && payment.GatewayTransactionReference == reference, ct);

    public Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct) =>
        context.Orders.Where(order => order.OrderStatus == OrderStatus.Pending &&
                order.PaymentTransaction!.PaymentStatus == PaymentStatus.Pending &&
                ((order.Channel == OrderChannel.Online && order.CreatedAt <= onlineCutoff) ||
                 (order.Channel == OrderChannel.Counter && order.CreatedAt <= cashCutoff)))
            .OrderBy(order => order.CreatedAt).Take(100)
            .Include(order => order.PaymentTransaction).Include(order => order.VoucherRedemption).ToListAsync(ct);

    public Task<List<MemberMembership>> LoadCurrentMembershipsAsync(string[] userIds, DateOnly today, CancellationToken ct) =>
        context.MemberMemberships.AsNoTracking()
            .Where(membership => userIds.Contains(membership.MemberUserId) && membership.EndDate >= today &&
                (membership.MembershipStatus == MembershipStatus.Active ||
                 membership.MembershipStatus == MembershipStatus.Scheduled ||
                 membership.MembershipStatus == MembershipStatus.Frozen))
            .Include(membership => membership.PackageComponent).ToListAsync(ct);

    public Task<List<MemberMembership>> LoadDueMembershipsAsync(DateOnly today, CancellationToken ct) =>
        context.MemberMemberships.Where(membership =>
                (membership.MembershipStatus == MembershipStatus.Scheduled && membership.StartDate <= today) ||
                (membership.MembershipStatus == MembershipStatus.Active && membership.EndDate < today))
            .OrderBy(membership => membership.StartDate).Take(100).ToListAsync(ct);

    public void Add(Order order) => context.Orders.Add(order);
    public void Add(AuditLog audit) => context.AuditLogs.Add(audit);
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
