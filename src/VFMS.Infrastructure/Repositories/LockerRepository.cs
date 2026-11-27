using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Payments;

namespace VFMS.Infrastructure.Repositories;

public sealed class LockerRepository(VfmsDbContext db) : AuditedRepositoryBase(db), ILockerRepository
{
    public async Task<Result<T>> RunSerializableAsync<T>(Func<Task<Result<T>>> work, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            return Conflict<T>();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
        {
            return Conflict<T>();
        }
    }

    private static Result<T> Conflict<T>() =>
        Result<T>.Failure("Dữ liệu tủ vừa thay đổi. Vui lòng thử lại.", "LOCKER_CONFLICT", 409);

    // ── Users ────────────────────────────────────────────────────

    private IQueryable<ApplicationUser> Users(UserRole role) => Context.ApplicationUsers
        .Where(u => u.AccountStatus == AccountStatus.Active && !u.IsBanned &&
            u.UserRoles.Any(r => r.Role.RoleName == role.ToString()));

    public Task<ApplicationUser?> GetActiveUserInRoleAsync(string userId, UserRole role, CancellationToken cancellationToken = default) =>
        Users(role).SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);

    private IQueryable<MemberMembership> EligibleMemberships(DateOnly today) =>
        Context.MemberMemberships.Where(m => m.MembershipStatus == MembershipStatus.Active &&
            m.PackageComponent.ComponentType == ComponentType.GYM && m.StartDate <= today && m.EndDate >= today &&
            !m.MembershipFreezes.Any(f => (f.RequestStatus == FreezeRequestStatus.Approved ||
                f.RequestStatus == FreezeRequestStatus.Active || f.RequestStatus == FreezeRequestStatus.Completed) &&
                (f.ActualStartDate ?? f.RequestedStartDate) <= today && (f.ActualEndDate ?? f.RequestedEndDate) > today));

    public Task<bool> HasEligibleGymMembershipAsync(string memberId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var members = Users(UserRole.Member);
        return EligibleMemberships(today).AnyAsync(m => m.MemberUserId == memberId &&
            members.Any(u => u.UserId == m.MemberUserId), cancellationToken);
    }

    public async Task<IReadOnlyList<LockerMember>> SearchMembersAsync(int gymId, string search, DateTime now, DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var eligible = EligibleMemberships(today);
        var allocated = Allocations(Context.LockerRentalContracts, now);
        var unpaid = Unpaid(Context.LockerRentalContracts);
        search = search.ToLowerInvariant();
        return await Users(UserRole.Member).Where(u => u.GymId == gymId &&
                (u.FullName.ToLower().Contains(search) || u.PhoneNumber.Contains(search) || u.Email.ToLower().Contains(search)))
            .OrderBy(u => u.FullName).ThenBy(u => u.UserId).Take(25)
            .Select(u => new LockerMember(u.UserId, u.FullName, u.PhoneNumber,
                eligible.Any(m => m.MemberUserId == u.UserId) && !allocated.Any(r => r.CustomerUserId == u.UserId) &&
                !unpaid.Any(r => r.CustomerUserId == u.UserId), u.Email))
            .ToListAsync(cancellationToken);
    }

    // ── Lockers and pricing ──────────────────────────────────────

    public async Task<IReadOnlyList<Locker>> GetGymLockersAsync(int gymId, CancellationToken cancellationToken = default) =>
        await Context.Lockers.AsNoTracking().Include(l => l.LockerZone)
            .Where(l => l.LockerZone.GymId == gymId && l.LockerStatus != LockerStatus.Inactive)
            .OrderBy(l => l.LockerZone.ZoneName).ThenBy(l => l.LockerNumber).ToListAsync(cancellationToken);

    public Task<Locker?> GetGymLockerAsync(int lockerId, int gymId, CancellationToken cancellationToken = default) =>
        Context.Lockers.Include(l => l.LockerZone).ThenInclude(z => z.Gym)
            .SingleOrDefaultAsync(l => l.Id == lockerId && l.LockerZone.GymId == gymId, cancellationToken);

    public async Task<IReadOnlyList<LockerPricingPolicy>> GetActivePoliciesAsync(CancellationToken cancellationToken = default) =>
        await Context.LockerPricingPolicies.Where(p => p.PolicyStatus == PolicyStatus.Active).ToListAsync(cancellationToken);

    public Task<LockerPricingPolicy?> GetActivePolicyAsync(int id, CancellationToken cancellationToken = default) =>
        Context.LockerPricingPolicies.SingleOrDefaultAsync(p => p.Id == id && p.PolicyStatus == PolicyStatus.Active, cancellationToken);

    public Task<Gym?> GetGymAsync(int gymId, CancellationToken cancellationToken = default) =>
        Context.Gyms.SingleOrDefaultAsync(g => g.Id == gymId, cancellationToken);

    public async Task<IReadOnlyList<LockerZone>> GetZonesAsync(int gymId, CancellationToken cancellationToken = default) =>
        await Context.LockerZones.AsNoTracking()
            .Include(z => z.Lockers)
            .Where(z => z.GymId == gymId).OrderBy(z => z.ZoneName).ToListAsync(cancellationToken);

    public Task<LockerZone?> GetZoneAsync(int zoneId, int gymId, CancellationToken cancellationToken = default) =>
        Context.LockerZones.Include(z => z.Lockers).SingleOrDefaultAsync(z => z.Id == zoneId && z.GymId == gymId, cancellationToken);

    public Task<bool> LockerNumberExistsAsync(int gymId, string number, CancellationToken cancellationToken = default) =>
        Context.Lockers.AnyAsync(l => l.LockerZone.GymId == gymId && l.LockerNumber == number, cancellationToken);

    // ── Rentals ──────────────────────────────────────────────────

    private IQueryable<LockerRentalContract> Rentals => Context.LockerRentalContracts
        .Include(r => r.Locker).ThenInclude(l => l.LockerZone).ThenInclude(z => z.Gym)
        .Include(r => r.LockerPricingPolicy)
        .Include(r => r.Customer)
        .Include(r => r.ReplacementLocker).ThenInclude(l => l!.LockerZone)
        .Include(r => r.Payments).ThenInclude(p => p.Order).ThenInclude(o => o.VoucherRedemption)
        .Include(r => r.Payments).ThenInclude(p => p.Invoice)
        .AsSplitQuery();

    // GBR-19: in use, awaiting handover, or held by a live checkout.
    private static IQueryable<LockerRentalContract> Allocations(IQueryable<LockerRentalContract> rentals, DateTime now) =>
        rentals.Where(r => r.ContractStatus == LockerContractStatus.Active || r.ContractStatus == LockerContractStatus.Overdue ||
            (r.ContractStatus == LockerContractStatus.Expired && r.CancelledAt == null) ||
            r.ContractStatus == LockerContractStatus.PendingHandover ||
            (r.ContractStatus == LockerContractStatus.PendingPayment && r.Payments.Any(p =>
                p.LockerPurpose == LockerPaymentPurpose.Rental && p.Order.OrderStatus == OrderStatus.Pending && p.HoldExpiresAt > now)));

    public Task<bool> HasAllocationAsync(DateTime now, int? lockerId, string? customerId, int? excludingRentalId = null,
        CancellationToken cancellationToken = default) =>
        Allocations(Context.LockerRentalContracts, now).AnyAsync(r => r.Id != excludingRentalId &&
            (lockerId != null && (r.LockerId == lockerId || r.ReplacementLockerId == lockerId) ||
             customerId != null && r.CustomerUserId == customerId), cancellationToken);

    public async Task<IReadOnlyList<LockerRentalContract>> GetGymAllocationsAsync(int gymId, DateTime now,
        CancellationToken cancellationToken = default) =>
        await Allocations(Context.LockerRentalContracts.AsNoTracking(), now)
            .Where(r => r.Locker.LockerZone.GymId == gymId).OrderByDescending(r => r.Id).ToListAsync(cancellationToken);

    public Task<LockerRentalContract?> GetAllocationForLockerAsync(int lockerId, DateTime now, CancellationToken cancellationToken = default) =>
        Allocations(Rentals, now).SingleOrDefaultAsync(r => r.LockerId == lockerId || r.ReplacementLockerId == lockerId, cancellationToken);

    public Task<LockerRentalContract?> GetCurrentRentalAsync(string memberId, DateTime now, CancellationToken cancellationToken = default) =>
        Allocations(Rentals.AsNoTracking(), now)
            .Where(r => r.CustomerUserId == memberId && r.ContractStatus != LockerContractStatus.PendingPayment)
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<LockerRentalContract?> GetRentalAsync(int id, int gymId, string? customerId = null,
        CancellationToken cancellationToken = default) =>
        Rentals.SingleOrDefaultAsync(r => r.Id == id && r.Locker.LockerZone.GymId == gymId &&
            (customerId == null || r.CustomerUserId == customerId), cancellationToken);

    public async Task<PagedResponse<LockerRentalContract>> GetRentalHistoryAsync(string memberId, int page,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 10;
        var query = Rentals.AsNoTracking().Where(r => r.CustomerUserId == memberId &&
            (r.ContractStatus == LockerContractStatus.Terminated || r.ContractStatus == LockerContractStatus.Abandoned ||
             r.ContractStatus == LockerContractStatus.Cancelled) &&
            (r.HandedOverAt != null || r.Payments.Any(p => p.LockerPurpose == LockerPaymentPurpose.Rental &&
                p.PaymentStatus == PaymentStatus.Paid)));
        var total = await query.CountAsync(cancellationToken);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(page, pageSize, total, items);
    }

    public async Task<IReadOnlyList<LockerRentalContract>> GetRegistryAsync(int gymId, DateTime now, DateOnly today, string? search,
        bool overdueOnly, CancellationToken cancellationToken = default)
    {
        var query = Allocations(Rentals.AsNoTracking(), now)
            .Where(r => r.Locker.LockerZone.GymId == gymId &&
                (overdueOnly || r.ContractStatus != LockerContractStatus.PendingPayment));
        if (overdueOnly)
            query = query.Where(r => r.ContractStatus != LockerContractStatus.PendingPayment &&
                (r.ExpiresAt <= now || r.ContractStatus != LockerContractStatus.PendingHandover &&
                    r.ExpiresAt == null && r.EndDate < today));
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.ContractNumber.ToLower().Contains(search) || r.Locker.LockerNumber.ToLower().Contains(search) ||
                r.Customer.FullName.ToLower().Contains(search) || r.Customer.PhoneNumber.Contains(search) ||
                r.Customer.Email.ToLower().Contains(search) || r.Payments.Any(p => p.LockerPurpose == LockerPaymentPurpose.Rental &&
                    p.Invoice!.InvoiceNumber.ToLower().Contains(search)));
        }
        return await query.OrderBy(r => r.EndDate).ThenBy(r => r.Id).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LockerRentalContract>> GetExpiredReplacementsAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await Rentals.Where(r => r.ReplacementLockerId != null && r.ExpiresAt <= now)
            .OrderBy(r => r.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LockerRentalContract>> GetAwaitingHandoverAsync(int take,
        CancellationToken cancellationToken = default) =>
        await Rentals.Where(r => r.ContractStatus == LockerContractStatus.PendingHandover)
            .OrderBy(r => r.Id).Take(take).ToListAsync(cancellationToken);

    private static IQueryable<LockerRentalContract> OpenMonthly(IQueryable<LockerRentalContract> rentals) =>
        rentals.Where(r => r.ReturnedAt == null && r.ExpiresAt != null &&
            (r.ContractStatus == LockerContractStatus.Active || r.ContractStatus == LockerContractStatus.Overdue ||
             r.ContractStatus == LockerContractStatus.Expired) &&
            r.LockerPricingPolicy.DurationMonths != 0);

    public async Task<IReadOnlyList<LockerRentalContract>> GetDueRemindersAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await OpenMonthly(Rentals).Where(r => r.ReminderSentAt == null && r.ExpiresAt > now && r.ExpiresAt <= now.AddDays(7))
            .OrderBy(r => r.Id).Take(take).ToListAsync(cancellationToken);

    // Overdue day 5 starts 4 days after the expiry; after day 7 the locker is cleared, so no notice is sent.
    public async Task<IReadOnlyList<LockerRentalContract>> GetDueOverdueNoticesAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await OpenMonthly(Rentals).Where(r => r.FinalNoticeAt == null && r.ExpiresAt <= now.AddDays(-4) && r.ExpiresAt > now.AddDays(-7))
            .OrderBy(r => r.Id).Take(take).ToListAsync(cancellationToken);

    private static IQueryable<LockerRentalContract> Unpaid(IQueryable<LockerRentalContract> rentals) =>
        rentals.Where(r => r.ContractStatus == LockerContractStatus.Abandoned && r.OutstandingAmount > 0 &&
            r.OutstandingPaidAt == null);

    public async Task<decimal> GetUnpaidAmountAsync(string memberId, CancellationToken cancellationToken = default) =>
        (await Unpaid(Context.LockerRentalContracts).Where(r => r.CustomerUserId == memberId)
            .Select(r => r.OutstandingAmount).ToListAsync(cancellationToken)).Sum(a => a ?? 0);

    public async Task<IReadOnlyList<LockerRentalContract>> GetUnpaidRentalsAsync(int gymId, string? search,
        CancellationToken cancellationToken = default)
    {
        var query = Unpaid(Rentals.AsNoTracking()).Where(r => r.Locker.LockerZone.GymId == gymId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLowerInvariant();
            query = query.Where(r => r.ContractNumber.ToLower().Contains(search) || r.Locker.LockerNumber.ToLower().Contains(search) ||
                r.Customer.FullName.ToLower().Contains(search) || r.Customer.PhoneNumber.Contains(search) ||
                r.Customer.Email.ToLower().Contains(search));
        }
        return await query.OrderBy(r => r.Id).Take(100).ToListAsync(cancellationToken);
    }

    // ── Payments ─────────────────────────────────────────────────

    public async Task<PaymentTransaction?> GetChargeByOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var id = await Context.PaymentTransactions.Where(p => p.OrderId == orderId)
            .Select(p => (int?)p.LockerRentalContractId).SingleOrDefaultAsync(cancellationToken);
        if (id is null)
            return null;
        var rental = await Rentals.SingleAsync(r => r.Id == id, cancellationToken);
        return rental.Payments.Single(p => p.OrderId == orderId);
    }

    public Task<PaymentTransaction?> GetCheckoutAsync(string orderNumber, int gymId, string? customerId,
        CancellationToken cancellationToken = default) =>
        Context.PaymentTransactions.AsNoTracking().Include(p => p.Order).ThenInclude(o => o.VoucherRedemption).Include(p => p.Invoice)
            .SingleOrDefaultAsync(p => p.Order.OrderNumber == orderNumber && p.LockerRentalContract!.Locker.LockerZone.GymId == gymId &&
                (customerId == null || p.LockerRentalContract!.CustomerUserId == customerId), cancellationToken);

    public async Task<IReadOnlyList<PaymentTransaction>> GetStaleChargesAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await Context.PaymentTransactions.Include(p => p.LockerRentalContract!).ThenInclude(r => r.Locker)
            .Include(p => p.Order).ThenInclude(o => o.VoucherRedemption).Where(p => p.LockerRentalContractId != null && p.HoldExpiresAt <= now &&
                (p.Order.OrderStatus == OrderStatus.Pending ||
                p.LockerPurpose == LockerPaymentPurpose.Rental && p.LockerRentalContract!.ContractStatus == LockerContractStatus.PendingPayment &&
                p.Order.OrderStatus == OrderStatus.Expired)).OrderBy(p => p.Id).Take(take).ToListAsync(cancellationToken);

    public Task<bool> IsTransferRecordedAsync(PaymentTransaction payment, string reference, CancellationToken cancellationToken = default) =>
        CheckoutRefunds.IsRecorded(Context, payment, reference, cancellationToken);

    public void RecordExtraTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now) =>
        CheckoutRefunds.RecordExtraTransfer(Context, payment, transfer, now);

    public void RecordUnappliedTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now) =>
        CheckoutRefunds.RecordUnappliedTransfer(Context, payment, transfer, now);

    public void RequestRefund(PaymentTransaction payment, string reason, DateTime now) =>
        CheckoutRefunds.Request(Context, payment, reason, now);

    public void ResolveRefund(PaymentTransaction payment, string actor, PaymentMethod method, string? reference, DateTime now) =>
        CheckoutRefunds.Resolve(Context, payment, actor, true, method, reference, now);

    // ── Clearances (GBR-20) ──────────────────────────────────────

    public Task<int?> GetOpenClearanceIdAsync(int rentalId, CancellationToken cancellationToken = default) =>
        Context.LockerIncidentLogs.Where(i => i.LockerRentalContractId == rentalId && i.VerifiedAt == null)
            .Select(i => (int?)i.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LockerIncidentLog>> GetOpenClearancesAsync(int gymId, int take,
        CancellationToken cancellationToken = default) =>
        await Context.LockerIncidentLogs.AsNoTracking().Where(i => i.LockerRentalContractId != null &&
                i.VerifiedAt == null && i.Locker.LockerZone.GymId == gymId &&
                (i.LockerRentalContract!.ContractStatus == LockerContractStatus.Active ||
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.Overdue ||
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.Expired ||
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.PendingHandover))
            .OrderBy(i => i.Id).Take(take).ToListAsync(cancellationToken);

    public Task<LockerIncidentLog?> GetClearanceAsync(int id, int gymId, CancellationToken cancellationToken = default) =>
        Context.LockerIncidentLogs.SingleOrDefaultAsync(i => i.Id == id &&
            i.LockerRentalContractId != null && i.Locker.LockerZone.GymId == gymId, cancellationToken);
}
