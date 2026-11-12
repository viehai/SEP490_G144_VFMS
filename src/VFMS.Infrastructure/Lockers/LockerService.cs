using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService(VfmsDbContext db, TimeProvider clock,
    IMembershipPurchaseRepository commerce, IMembershipPaymentGateway gateway) : ILockerPaymentFulfillment
{
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);
    private const string Reconciliation = "LOCKER_RECONCILIATION_REQUIRED";
    private DateTimeOffset Now => clock.GetUtcNow();
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime Local(DateTime utc) => utc.Add(LocalOffset);
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(Local(utc));

    private IQueryable<ApplicationUser> Users(UserRole role) => db.ApplicationUsers
        .Where(u => u.AccountStatus == AccountStatus.Active && !u.IsBanned &&
            u.UserRoles.Any(r => r.Role.RoleName == role.ToString()));

    private Task<ApplicationUser?> Actor(string id, UserRole role, CancellationToken ct) =>
        Users(role).SingleOrDefaultAsync(u => u.UserId == id, ct);

    private IQueryable<LockerRentalContract> Rentals => db.LockerRentalContracts
        .Include(r => r.Locker).ThenInclude(l => l.LockerZone).ThenInclude(z => z.Gym)
        .Include(r => r.LockerPricingPolicy).Include(r => r.Customer)
        .Include(r => r.Payments).ThenInclude(p => p.Order).ThenInclude(o => o.PaymentTransaction).ThenInclude(p => p!.Invoice)
        .AsSplitQuery();

    private static IQueryable<LockerRentalContract> Allocations(IQueryable<LockerRentalContract> rentals, DateTime now) =>
        rentals.Where(r => r.ContractStatus == LockerContractStatus.Active || r.ContractStatus == LockerContractStatus.Overdue ||
            (r.ContractStatus == LockerContractStatus.Expired && r.CancelledAt == null) ||
            (r.ContractStatus == LockerContractStatus.PendingHandover && r.CollectBy > now) ||
            (r.ContractStatus == LockerContractStatus.PendingPayment && r.Payments.Any(p =>
                p.Purpose == LockerPaymentPurpose.Rental && p.Order.OrderStatus == OrderStatus.Pending && p.HoldExpiresAt > now)));

    private IQueryable<MemberMembership> EligibleMemberships(DateTime now)
    {
        var today = LocalDate(now);
        return db.MemberMemberships.Where(m => m.MembershipStatus == MembershipStatus.Active &&
            m.PackageComponent.ComponentType == ComponentType.GYM && m.StartDate <= today && m.EndDate >= today &&
            !m.MembershipFreezes.Any(f => (f.RequestStatus == FreezeRequestStatus.Approved ||
                f.RequestStatus == FreezeRequestStatus.Active || f.RequestStatus == FreezeRequestStatus.Completed) &&
                (f.ActualStartDate ?? f.RequestedStartDate) <= today && (f.ActualEndDate ?? f.RequestedEndDate) > today));
    }

    private Task<bool> Eligible(string member, DateTime now, CancellationToken ct)
    {
        var members = Users(UserRole.Member);
        return EligibleMemberships(now).AnyAsync(m => m.MemberUserId == member &&
            members.Any(u => u.UserId == m.MemberUserId), ct);
    }

    private async Task<Result<T>> Write<T>(Func<Task<Result<T>>> action, CancellationToken ct)
    {
        try { return await commerce.ExecuteInSerializableTransactionAsync(action, ct); }
        catch (SqlException ex) when (ex.Number == 1205)
        { return Conflict<T>("Dữ liệu tủ vừa thay đổi. Vui lòng thử lại."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
        { return Conflict<T>("Dữ liệu tủ vừa thay đổi. Vui lòng thử lại."); }
    }

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, "LOCKER_VALIDATION");
    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, "LOCKER_CONFLICT", 409);
    private static Result<T> Missing<T>() => Result<T>.Failure("Không tìm thấy dữ liệu tủ.", "LOCKER_NOT_FOUND", 404);
    private static Result<T> Forbidden<T>() => Result<T>.Failure("Tài khoản không có quyền thực hiện.", "LOCKER_FORBIDDEN", 403);

    private static int Months(RentalType type) => type switch
    {
        RentalType.Monthly => 1,
        RentalType.Quarterly => 3,
        RentalType.HalfYearly => 6,
        RentalType.Yearly => 12,
        _ => 0
    };

    private static RentalType Type(LockerRentalContract rental) =>
        rental.RentalTypeSnapshot ?? rental.LockerPricingPolicy.RentalType;

    private static DateTime RentalExpiry(LockerRentalContract rental) =>
        rental.ExpiresAt ?? rental.EndDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc).Subtract(LocalOffset);

    private static int OverdueDays(LockerRentalContract rental, DateTime now) =>
        (int)Math.Max(0, Math.Ceiling((now - RentalExpiry(rental)).TotalDays));

    private static decimal OverdueFees(LockerRentalContract rental, DateTime now) =>
        Type(rental) == RentalType.Daily ? 0 :
        OverdueDays(rental, now) * (rental.OverdueDailyFeeSnapshot ?? rental.LockerPricingPolicy.OverdueDailyFee);

    private static bool InUse(LockerRentalContract rental) =>
        rental.CancelledAt == null && rental.ContractStatus is
            LockerContractStatus.Active or LockerContractStatus.Overdue or LockerContractStatus.Expired;

    private static string Status(LockerRentalContract rental, DateTime now) =>
        InUse(rental) ? (now > RentalExpiry(rental) ? "Overdue" : "Active") : rental.ContractStatus.ToString();

    private static string SlotStatus(Locker locker, LockerRentalContract? allocation, DateTime now)
    {
        if (locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged)
            return "Maintenance";
        if (allocation is null)
            return locker.LockerStatus == LockerStatus.Rented ? "Occupied" : "Available";
        return Status(allocation, now) switch
        {
            "Active" => "Occupied",
            "Overdue" => "Overdue",
            _ => "Reserved"
        };
    }

    private static LockerSlot Slot(Locker locker, string status) =>
        new(locker.Id, locker.LockerNumber, locker.LockerZone.ZoneName, locker.LockerZone.FloorNumber, status);

    private static LockerRentalView View(LockerRentalContract rental, DateTime now)
    {
        var status = Status(rental, now);
        var overdue = InUse(rental) ? OverdueDays(rental, now) : 0;
        var initial = rental.Payments.SingleOrDefault(p => p.Purpose == LockerPaymentPurpose.Rental);
        var lastRenewal = rental.Payments.Where(p => p.Purpose == LockerPaymentPurpose.Renewal &&
            p.Order.OrderStatus == OrderStatus.Paid).OrderByDescending(p => p.Id).FirstOrDefault();
        var settlement = rental.Payments.SingleOrDefault(p => p.Purpose == LockerPaymentPurpose.Settlement &&
            p.Order.OrderStatus == OrderStatus.Paid);
        return new(rental.Id, rental.ContractNumber, Slot(rental.Locker, InUse(rental) ||
                rental.ContractStatus == LockerContractStatus.PendingHandover ? SlotStatus(rental.Locker, rental, now) :
                SlotStatus(rental.Locker, null, now)), Type(rental).ToString(), status, rental.RentalPrice,
            InUse(rental) ? rental.DepositAmount : 0,
            rental.HandedOverAt is { } handover ? Utc(handover) : null,
            InUse(rental) || rental.ExpiresAt != null ? Utc(RentalExpiry(rental)) : null,
            rental.CollectBy is { } collectBy ? Utc(collectBy) : null,
            overdue, overdue > 0 ? OverdueFees(rental, now) : 0,
            overdue > 0 ? Utc(RentalExpiry(rental).AddDays(7)) : null,
            Type(rental) != RentalType.Daily && InUse(rental) && overdue <= 7 &&
                rental.Locker.LockerStatus is not (LockerStatus.Maintenance or LockerStatus.Damaged),
            status == "PendingHandover" ? "Mang mã hợp đồng hoặc hóa đơn đến lễ tân để nhận chìa khóa." : null,
            initial?.Order.PaymentTransaction?.Invoice?.InvoiceNumber, rental.OutstandingAmount,
            status == "Active" && Type(rental) != RentalType.Daily && RentalExpiry(rental) - now <= TimeSpan.FromDays(7),
            lastRenewal?.RenewalMonths ?? Months(Type(rental)), rental.SettledAt is { } settled ? Utc(settled) : null,
            initial is { RefundReason: "DailyDepositReturn", RefundedAt: not null } ? initial.RefundAmount : 0,
            settlement?.Order.PaymentTransaction?.Invoice?.InvoiceNumber);
    }

    private static LockerPolicy Policy(LockerPricingPolicy p) =>
        new(p.Id, p.PolicyName, p.RentalType.ToString(), Months(p.RentalType), p.RentalPrice, p.DepositAmount,
            p.OverdueDailyFee, p.KeyReplacementFee);

    private void Audit(string? actor, LockerRentalContract rental, string value, DateTime now) =>
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor, ActionType = AuditActionType.Update, AuditEntity = nameof(LockerRentalContract),
            EntityId = rental.Id, NewValue = value, OccurredAt = now
        });

    private void Notify(LockerRentalContract rental, string title, string message, DateTime now) =>
        db.Notifications.Add(new Notification
        {
            RecipientUserId = rental.CustomerUserId, NotificationType = "Locker", Title = title, Message = message,
            EntityType = nameof(LockerRentalContract), EntityId = rental.Id, NotifiedAt = now
        });

    public async Task<Result<IReadOnlyList<LockerSlot>>> Grid(string actor, UserRole role, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<IReadOnlyList<LockerSlot>>();
        var now = Now.UtcDateTime;
        var lockers = await db.Lockers.AsNoTracking().Include(l => l.LockerZone)
            .Where(l => l.LockerZone.GymId == user.GymId)
            .OrderBy(l => l.LockerZone.FloorNumber).ThenBy(l => l.LockerNumber).ToListAsync(ct);
        var rentals = await Allocations(db.LockerRentalContracts.AsNoTracking(), now)
            .Where(r => r.Locker.LockerZone.GymId == user.GymId).OrderByDescending(r => r.Id).ToListAsync(ct);
        var byLocker = rentals.ToLookup(r => r.LockerId);
        return Result<IReadOnlyList<LockerSlot>>.Success(lockers.Select(l =>
            Slot(l, SlotStatus(l, byLocker[l.Id].FirstOrDefault(), now))).ToArray());
    }

    public async Task<Result<IReadOnlyList<LockerPolicy>>> Pricing(string actor, UserRole role, CancellationToken ct)
    {
        if (await Actor(actor, role, ct) is null)
            return Forbidden<IReadOnlyList<LockerPolicy>>();
        var policies = await db.LockerPricingPolicies.AsNoTracking().Where(p => p.PolicyStatus == PolicyStatus.Active &&
            (role != UserRole.Member || p.RentalType != RentalType.Daily)).ToListAsync(ct);
        return Result<IReadOnlyList<LockerPolicy>>.Success(policies.Select(Policy).OrderBy(p => p.Months).ToArray());
    }

    public async Task<Result<LockerRentalView?>> MyRental(string actor, CancellationToken ct)
    {
        if (await Actor(actor, UserRole.Member, ct) is null)
            return Forbidden<LockerRentalView?>();
        var now = Now.UtcDateTime;
        var rental = await Allocations(Rentals.AsNoTracking(), now)
            .Where(r => r.CustomerUserId == actor && r.ContractStatus != LockerContractStatus.PendingPayment)
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        return Result<LockerRentalView?>.Success(rental is null ? null : View(rental, now));
    }
}
