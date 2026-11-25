using System.Data;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed partial class MembershipPurchaseRepository(VfmsDbContext context) : IMembershipPurchaseRepository
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

    public Task<FreezePolicy?> GetPurchaseFreezePolicyAsync(CancellationToken ct) =>
        context.FreezePolicies.AsNoTracking().Where(policy => policy.IsGlobal && policy.PolicyStatus == PolicyStatus.Active)
            .SingleOrDefaultAsync(ct);

    public Task<bool> AnyBookableYogaClassAsync(DateTime nowUtc, CancellationToken ct) =>
        context.YogaClasses.AnyAsync(YogaClassRules.Bookable(nowUtc), ct);

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

    public async Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct, bool membershipOnly = false)
    {
        var orders = forUpdate && context.Database.IsSqlServer()
            ? context.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK) WHERE [OrderNumber] = {orderNumber}")
            : context.Orders;
        var query = orders.Where(order => order.OrderNumber == orderNumber);
        if (purchaserId is not null)
            query = query.Where(order => order.PurchaserUserId == purchaserId);
        if (!forUpdate)
            query = query.AsNoTracking().Include(order => order.Items).ThenInclude(item => item.MemberMemberships);
        else
            query = query.Include(order => order.Purchaser)
                .Include(order => order.Items).ThenInclude(item => item.Package).ThenInclude(plan => plan.Components);

        var order = await query.Include(order => order.PaymentTransaction)!.ThenInclude(payment => payment!.Invoice)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        if (order is not null)
        {
            if (membershipOnly && (order.PaymentTransaction?.LockerRentalContractId is not null ||
                await context.PTPurchases.IgnoreQueryFilters().AnyAsync(p => p.OrderId == order.Id, ct)))
                return null;
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

    public async Task<VoucherCapacity> ReadVoucherCapacityAsync(int voucherId, ApplicationUser purchaser, CancellationToken ct) =>
        (await VoucherCapacityReader.ReadForUserAsync(context, [voucherId], purchaser.UserId, ct))[voucherId];

    public Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct)
    {
        var orders = context.Database.IsSqlServer()
            ? context.Orders.FromSqlRaw("SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK)")
            : context.Orders;
        return orders.IgnoreQueryFilters().Where(order => !order.IsDeleted && order.OrderStatus == OrderStatus.Pending &&
                !order.PaymentTransaction!.IsDeleted &&
                order.PaymentTransaction!.PaymentStatus == PaymentStatus.Pending &&
                !context.PTPurchases.Any(p => p.OrderId == order.Id) &&
                order.PaymentTransaction.LockerRentalContractId == null &&
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
            .OrderByDescending(membership => membership.MembershipStatus == MembershipStatus.AwaitingActivation)
            .ThenByDescending(membership => membership.EndDate).ThenByDescending(membership => membership.Id)
            .Select(membership => new OwnedMembership(membership.Id, membership.OrderItem.PackageId,
                membership.OrderItem.PackageNameSnapshot, membership.PackageComponent.ComponentType,
                membership.StartDate, membership.EndDate, membership.MembershipStatus,
                membership.OrderItem.Order.PaymentTransaction!.PaidAt ?? membership.OrderItem.Order.CompletedAt)
            {
                PackageComponentId = membership.PackageComponentId,
                ComponentTermsSnapshot = membership.OrderItem.ComponentTermsSnapshot
            })
            .ToListAsync(ct);

    public Task<OwnedMembershipDetail?> FindOwnedMembershipAsync(string memberId, int membershipId, CancellationToken ct) =>
        OwnedMemberships(memberId).Where(membership => membership.Id == membershipId)
            .Select(membership => new OwnedMembershipDetail(
                new OwnedMembership(membership.Id, membership.OrderItem.PackageId,
                    membership.OrderItem.PackageNameSnapshot, membership.PackageComponent.ComponentType,
                    membership.StartDate, membership.EndDate, membership.MembershipStatus,
                    membership.OrderItem.Order.PaymentTransaction!.PaidAt ?? membership.OrderItem.Order.CompletedAt)
                {
                    PackageComponentId = membership.PackageComponentId,
                    ComponentTermsSnapshot = membership.OrderItem.ComponentTermsSnapshot
                },
                membership.OrderItem.Order.PaymentTransaction!.PaidAt ?? membership.OrderItem.Order.CompletedAt,
                membership.OrderItem.Order.PurchaserUserId == memberId
                    ? membership.OrderItem.Order.PaymentTransaction!.Invoice!.InvoiceNumber : null,
                membership.TotalFrozenDays, membership.FreezeCount,
                membership.MembershipFreezes.OrderByDescending(period => period.RequestedStartDate)
                    .ThenByDescending(period => period.Id)
                    .Select(period => new OwnedMembershipFreeze(period.RequestedStartDate, period.RequestedEndDate,
                        period.ActualStartDate, period.ActualEndDate, period.RequestStatus, period.CreatedAt,
                        period.RequestStatus == FreezeRequestStatus.Rejected ? period.ReviewNote : null)).ToList(),
                membership.GrantedFreezeAllowanceDays, membership.GrantedFreezeCount, membership.GrantedMinFreezeDays, membership.GrantedMaxFreezeDays))
            .SingleOrDefaultAsync(ct);

    private IQueryable<MemberMembership> OwnedMemberships(string memberId) =>
        context.MemberMemberships.AsNoTracking().Where(membership => membership.MemberUserId == memberId);

    public Task<List<MemberMembership>> LoadOwnedOrderMembershipsAsync(string memberId, int membershipId, CancellationToken ct)
    {
        var memberships = context.MemberMemberships.Where(row => row.MemberUserId == memberId);
        var orderItems = memberships.Where(row => row.Id == membershipId).Select(row => row.OrderItemId);
        return memberships.Where(row => orderItems.Contains(row.OrderItemId))
            .Include(row => row.OrderItem).ThenInclude(item => item.Order).ThenInclude(order => order.PaymentTransaction)
            .ToListAsync(ct);
    }

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

    public Task<string[]> FindMembershipUsersDueAsync(DateOnly today, DateTime activationCutoffUtc, CancellationToken ct) =>
        DueMemberships(today, activationCutoffUtc).Select(row => row.MemberUserId).Distinct()
            .OrderBy(id => id).Take(100).ToArrayAsync(ct);

    public Task<List<MemberMembership>> LoadDueMembershipsAsync(
        string[] userIds, DateOnly today, DateTime activationCutoffUtc, CancellationToken ct) =>
        DueMemberships(today, activationCutoffUtc).Where(row => userIds.Contains(row.MemberUserId))
            .Include(row => row.OrderItem).ThenInclude(item => item.Order).ThenInclude(order => order.PaymentTransaction)
            .ToListAsync(ct);

    private IQueryable<MemberMembership> DueMemberships(DateOnly today, DateTime activationCutoffUtc) =>
        context.MemberMemberships.Where(row =>
            (row.MembershipStatus == MembershipStatus.Scheduled && row.StartDate <= today) ||
            (row.MembershipStatus == MembershipStatus.Active && row.EndDate < today) ||
            (row.MembershipStatus == MembershipStatus.AwaitingActivation && row.OrderItem.Order.OrderStatus == OrderStatus.Paid &&
                (row.OrderItem.Order.PaymentTransaction!.PaidAt ?? row.OrderItem.Order.CompletedAt) < activationCutoffUtc));

    public void Add(Order order) => context.Orders.Add(order);
    public void Add(AuditLog audit) => context.AuditLogs.Add(audit);
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
