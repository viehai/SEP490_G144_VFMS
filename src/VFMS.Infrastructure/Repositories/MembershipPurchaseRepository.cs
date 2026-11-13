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

    public async Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct)
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

        var order = await query.Include(order => order.PaymentTransaction)!.ThenInclude(payment => payment!.Invoice)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        if (order is not null)
        {
            var redemptions = context.VoucherRedemptions.IgnoreQueryFilters();
            if (!forUpdate) redemptions = redemptions.AsNoTracking();
            order.VoucherRedemption = await redemptions.SingleOrDefaultAsync(row => row.OrderId == order.Id, ct);
        }
        return order;
    }

    public Task<bool> SepayReferenceUsedAsync(string reference, CancellationToken ct) =>
        context.PaymentTransactions.IgnoreQueryFilters().AnyAsync(
            payment => payment.GatewayName == "SePay" && payment.GatewayTransactionReference == reference, ct);

    public Task<Voucher?> FindVoucherForUpdateAsync(string normalizedCode, CancellationToken ct)
    {
        // Voucher is always locked before the order during Apply. Do not join the mutable
        // voucher from payment/order reads, which use the opposite order-first lifecycle lock.
        var vouchers = context.Database.IsSqlServer()
            ? context.Vouchers.FromSqlInterpolated($"SELECT * FROM [Vouchers] WITH (XLOCK, HOLDLOCK) WHERE UPPER(LTRIM(RTRIM([Code]))) = {normalizedCode}")
            : context.Vouchers.Where(voucher => voucher.Code.Trim().ToUpper() == normalizedCode);
        return vouchers.SingleOrDefaultAsync(voucher => !voucher.IsDeleted, ct);
    }

    public async Task<(int Confirmed, int Reserved)> CountVoucherCapacityAsync(int voucherId, CancellationToken ct)
    {
        // Retain expired reservations until JOB-01 releases them. Counting only redemption
        // state avoids reading another order while its Apply call waits for the voucher lock.
        var rows = context.Database.IsSqlServer()
            ? context.VoucherRedemptions.FromSqlInterpolated($"SELECT * FROM [VoucherRedemptions] WITH (UPDLOCK, HOLDLOCK) WHERE [VoucherId] = {voucherId}")
            : context.VoucherRedemptions.Where(row => row.VoucherId == voucherId);
        var counts = await rows.IgnoreQueryFilters()
            .Where(row => row.RedemptionStatus == VoucherRedemptionStatus.Confirmed || row.RedemptionStatus == VoucherRedemptionStatus.Reserved)
            .GroupBy(row => row.RedemptionStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        return (counts.SingleOrDefault(row => row.Status == VoucherRedemptionStatus.Confirmed)?.Count ?? 0,
            counts.SingleOrDefault(row => row.Status == VoucherRedemptionStatus.Reserved)?.Count ?? 0);
    }

    public Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct)
    {
        var orders = context.Database.IsSqlServer()
            ? context.Orders.FromSqlRaw("SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK)")
            : context.Orders;
        return orders.IgnoreQueryFilters().Where(order => !order.IsDeleted && order.OrderStatus == OrderStatus.Pending &&
                !order.PaymentTransaction!.IsDeleted &&
                order.PaymentTransaction!.PaymentStatus == PaymentStatus.Pending &&
                ((order.Channel == OrderChannel.Online && order.CreatedAt <= onlineCutoff) ||
                 (order.Channel == OrderChannel.Counter && order.CreatedAt <= cashCutoff)))
            .OrderBy(order => order.CreatedAt).Take(100)
            .Include(order => order.PaymentTransaction).Include(order => order.VoucherRedemption).ToListAsync(ct);
    }

    public Task<List<MemberMembership>> LoadCurrentMembershipsAsync(string[] userIds, DateOnly today, CancellationToken ct) =>
        context.MemberMemberships.AsNoTracking()
            .Where(membership => userIds.Contains(membership.MemberUserId) && membership.EndDate >= today &&
                (membership.MembershipStatus == MembershipStatus.Active ||
                 membership.MembershipStatus == MembershipStatus.Scheduled ||
                 membership.MembershipStatus == MembershipStatus.Frozen ||
                 membership.MembershipStatus == MembershipStatus.Suspended))
            .Include(membership => membership.PackageComponent).ToListAsync(ct);

    public Task<List<OwnedMembership>> ListOwnedMembershipsAsync(string memberId, CancellationToken ct) =>
        OwnedMemberships(memberId)
            .OrderByDescending(membership => membership.EndDate).ThenByDescending(membership => membership.Id)
            .Select(membership => new OwnedMembership(membership.Id, membership.OrderItem.PackageId,
                membership.OrderItem.PackageNameSnapshot, membership.PackageComponent.ComponentType,
                membership.StartDate, membership.EndDate, membership.MembershipStatus))
            .ToListAsync(ct);

    public Task<OwnedMembershipDetail?> FindOwnedMembershipAsync(string memberId, int membershipId, CancellationToken ct) =>
        OwnedMemberships(memberId).Where(membership => membership.Id == membershipId)
            .Select(membership => new OwnedMembershipDetail(
                new OwnedMembership(membership.Id, membership.OrderItem.PackageId,
                    membership.OrderItem.PackageNameSnapshot, membership.PackageComponent.ComponentType,
                    membership.StartDate, membership.EndDate, membership.MembershipStatus),
                membership.OrderItem.Order.PaymentTransaction!.PaidAt ?? membership.OrderItem.Order.CompletedAt,
                membership.OrderItem.Order.PurchaserUserId == memberId
                    ? membership.OrderItem.Order.PaymentTransaction!.Invoice!.InvoiceNumber : null,
                membership.TotalFrozenDays, membership.FreezeCount,
                membership.MembershipFreezes.OrderByDescending(period => period.RequestedStartDate)
                    .ThenByDescending(period => period.Id)
                    .Select(period => new OwnedMembershipFreeze(period.RequestedStartDate, period.RequestedEndDate,
                        period.ActualStartDate, period.ActualEndDate, period.RequestStatus)).ToList()))
            .SingleOrDefaultAsync(ct);

    private IQueryable<MemberMembership> OwnedMemberships(string memberId) =>
        context.MemberMemberships.AsNoTracking().Where(membership => membership.MemberUserId == memberId);

    // General Policies configures renewal windows on the default gym.
    public Task<Gym?> GetRenewalPolicyGymAsync(CancellationToken ct) =>
        context.Gyms.AsNoTracking().OrderBy(gym => gym.Id).FirstOrDefaultAsync(ct);

    public Task<List<MemberMembership>> LoadRenewableMembershipsAsync(
        string memberId, int? membershipId, CancellationToken ct) =>
        context.MemberMemberships.AsNoTracking()
            .Where(membership => membership.MemberUserId == memberId &&
                membership.PackageComponent.ComponentType == ComponentType.GYM &&
                (membershipId == null || membership.Id == membershipId))
            .Include(membership => membership.OrderItem)
            .OrderByDescending(membership => membership.EndDate).ThenByDescending(membership => membership.Id)
            .ToListAsync(ct);

    public Task<List<Order>> LoadRenewalOrdersAsync(int[] membershipIds, bool forUpdate, CancellationToken ct)
    {
        var orders = forUpdate && context.Database.IsSqlServer()
            ? context.Orders.FromSqlRaw("SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK)")
            : context.Orders;
        return (forUpdate ? orders : orders.AsNoTracking())
            .Where(order => (order.OrderStatus == OrderStatus.Pending || order.OrderStatus == OrderStatus.Paid) &&
                order.Items.Any(item => item.RenewedFromMembershipId.HasValue &&
                    membershipIds.Contains(item.RenewedFromMembershipId.Value)))
            .Include(order => order.Items).Include(order => order.PaymentTransaction).Include(order => order.VoucherRedemption)
            .AsSplitQuery().ToListAsync(ct);
    }

    public Task<string?> FindRenewalPurchaserAsync(string orderNumber, CancellationToken ct) =>
        context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == orderNumber && order.Items.Any(item => item.RenewedFromMembershipId != null))
            .Select(order => order.PurchaserUserId).SingleOrDefaultAsync(ct);

    public Task<List<MemberMembership>> LoadDueMembershipsAsync(DateOnly today, CancellationToken ct) =>
        context.MemberMemberships.Where(membership =>
                (membership.MembershipStatus == MembershipStatus.Scheduled && membership.StartDate <= today) ||
                (membership.MembershipStatus == MembershipStatus.Active && membership.EndDate < today))
            .OrderBy(membership => membership.StartDate).Take(100).ToListAsync(ct);

    public void Add(Order order) => context.Orders.Add(order);
    public void Add(AuditLog audit) => context.AuditLogs.Add(audit);
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
