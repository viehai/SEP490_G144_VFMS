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
        return await Users(UserRole.Member).Where(u => u.GymId == gymId &&
                (u.FullName.Contains(search) || u.PhoneNumber.Contains(search)))
            .OrderBy(u => u.FullName).ThenBy(u => u.UserId).Take(25)
            .Select(u => new LockerMember(u.UserId, u.FullName, u.PhoneNumber,
                eligible.Any(m => m.MemberUserId == u.UserId) && !allocated.Any(r => r.CustomerUserId == u.UserId)))
            .ToListAsync(cancellationToken);
    }

    // ── Lockers and pricing ──────────────────────────────────────

    public async Task<IReadOnlyList<Locker>> GetGymLockersAsync(int gymId, CancellationToken cancellationToken = default) =>
        await Context.Lockers.AsNoTracking().Include(l => l.LockerZone)
            .Where(l => l.LockerZone.GymId == gymId)
            .OrderBy(l => l.LockerZone.FloorNumber).ThenBy(l => l.LockerNumber).ToListAsync(cancellationToken);

    public Task<Locker?> GetGymLockerAsync(int lockerId, int gymId, CancellationToken cancellationToken = default) =>
        Context.Lockers.Include(l => l.LockerZone).ThenInclude(z => z.Gym)
            .SingleOrDefaultAsync(l => l.Id == lockerId && l.LockerZone.GymId == gymId, cancellationToken);

    public async Task<IReadOnlyList<LockerPricingPolicy>> GetActivePoliciesAsync(CancellationToken cancellationToken = default) =>
        await Context.LockerPricingPolicies.Where(p => p.PolicyStatus == PolicyStatus.Active).ToListAsync(cancellationToken);

    public Task<LockerPricingPolicy?> GetActivePolicyAsync(int id, CancellationToken cancellationToken = default) =>
        Context.LockerPricingPolicies.SingleOrDefaultAsync(p => p.Id == id && p.PolicyStatus == PolicyStatus.Active, cancellationToken);

    // ── Rentals ──────────────────────────────────────────────────

    private IQueryable<LockerRentalContract> Rentals => Context.LockerRentalContracts
        .Include(r => r.Locker).ThenInclude(l => l.LockerZone).ThenInclude(z => z.Gym)
        .Include(r => r.LockerPricingPolicy)
        .Include(r => r.Customer)
        .Include(r => r.ReplacementLocker).ThenInclude(l => l!.LockerZone)
        .Include(r => r.Payments).ThenInclude(p => p.Order)
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
            query = query.Where(r => r.ContractNumber.Contains(search) || r.Locker.LockerNumber.Contains(search) ||
                r.Customer.FullName.Contains(search) || r.Payments.Any(p => p.LockerPurpose == LockerPaymentPurpose.Rental &&
                    p.Invoice!.InvoiceNumber.Contains(search)));
        return await query.OrderBy(r => r.EndDate).ThenBy(r => r.Id).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LockerRentalContract>> GetExpiredReplacementsAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await Rentals.Where(r => r.ReplacementLockerId != null && r.ExpiresAt <= now)
            .OrderBy(r => r.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LockerRentalContract>> GetPaidDailyAwaitingHandoverAsync(int take,
        CancellationToken cancellationToken = default) =>
        await Rentals.Where(r => r.ContractStatus == LockerContractStatus.PendingHandover &&
                (r.RentalTypeSnapshot == RentalType.Daily ||
                 r.RentalTypeSnapshot == null && r.LockerPricingPolicy.RentalType == RentalType.Daily))
            .OrderBy(r => r.Id).Take(take).ToListAsync(cancellationToken);

    public Task<LockerRentalContract?> GetNextPendingNoticeAsync(DateTime now, CancellationToken cancellationToken = default) =>
        Rentals.Where(r => r.ReturnedAt == null && r.FinalNoticeAt != null && r.FinalNoticeEmailSentAt == null &&
                (r.FinalNoticeRetryAt == null || r.FinalNoticeRetryAt <= now))
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task MarkNoticeEmailSentAsync(int rentalId, DateTime? finalNoticeAt, DateTime? retryAt, DateTime sentAt,
        CancellationToken cancellationToken = default) =>
        Context.LockerRentalContracts.Where(r => r.Id == rentalId && r.FinalNoticeAt == finalNoticeAt &&
                r.FinalNoticeRetryAt == retryAt)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.FinalNoticeEmailSentAt, sentAt), cancellationToken);

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
        Context.PaymentTransactions.AsNoTracking().Include(p => p.Order).Include(p => p.Invoice)
            .SingleOrDefaultAsync(p => p.Order.OrderNumber == orderNumber && p.LockerRentalContract!.Locker.LockerZone.GymId == gymId &&
                (customerId == null || p.LockerRentalContract!.CustomerUserId == customerId), cancellationToken);

    public async Task<IReadOnlyList<PaymentTransaction>> GetStaleChargesAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await Context.PaymentTransactions.Include(p => p.LockerRentalContract!).ThenInclude(r => r.Locker)
            .Include(p => p.Order).Where(p => p.LockerRentalContractId != null && p.HoldExpiresAt <= now &&
                (p.Order.OrderStatus == OrderStatus.Pending ||
                p.LockerPurpose == LockerPaymentPurpose.Rental && p.LockerRentalContract!.ContractStatus == LockerContractStatus.PendingPayment &&
                p.Order.OrderStatus == OrderStatus.Expired)).OrderBy(p => p.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentTransaction>> GetPendingRefundsAsync(int gymId, int take,
        CancellationToken cancellationToken = default) =>
        await Context.PaymentTransactions.AsNoTracking().Include(p => p.Order).Include(p => p.Invoice)
            .Where(p => p.LockerRentalContract!.Locker.LockerZone.GymId == gymId &&
                p.RefundAmount > 0 && p.RefundStatus != "Refunded")
            .OrderBy(p => p.Id).Take(take).ToListAsync(cancellationToken);

    public Task LoadSourcePaymentAsync(PaymentTransaction charge, CancellationToken cancellationToken = default) =>
        Context.Entry(charge).Reference(p => p.SourcePaymentTransaction).Query().Include(p => p.Order).LoadAsync(cancellationToken);

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
