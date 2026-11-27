using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class LockerService(ILockerRepository repo, TimeProvider clock,
    IMembershipPurchaseRepository commerce, IMembershipPaymentGateway gateway) : ILockerService, ILockerPaymentFulfillment
{
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(20);

    /// <summary>RefundReason of the key deposit paid back at termination (UC 5.3); not a checkout refund.</summary>
    public const string DepositReturnReason = "KeyDepositReturn";
    private const string LegacyDepositReturnReason = "DailyDepositReturn";
    public static bool IsDepositReturn(string? reason) => reason is DepositReturnReason or LegacyDepositReturnReason;

    private DateTimeOffset Now => clock.GetUtcNow();
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime Local(DateTime utc) => utc.Add(LocalOffset);
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(Local(utc));
    private static DateTime Midnight(DateOnly localDate) =>
        localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Subtract(LocalOffset);

    private static bool OpenForDailyRental(Gym gym, DateTime now)
    {
        var time = TimeOnly.FromDateTime(Local(now));
        return gym.OpenTime is { } open && gym.CloseTime is { } close && open <= time && time < close;
    }

    // GBR-112: closing time of the local date; 23:59 when the gym profile has no Close Time.
    private static DateTime AtClose(DateOnly localDate, Gym gym) =>
        localDate.ToDateTime(gym.CloseTime ?? new TimeOnly(23, 59), DateTimeKind.Utc).Subtract(LocalOffset);

    private static void SetMonthlyTerm(LockerRentalContract rental, DateOnly start, int months)
    {
        rental.StartDate = start;
        rental.EndDate = start.AddMonths(months).AddDays(-1);
        rental.ExpiresAt = Midnight(rental.EndDate.AddDays(1));
    }

    private Task<ApplicationUser?> Actor(string id, UserRole role, CancellationToken ct) =>
        repo.GetActiveUserInRoleAsync(id, role, ct);

    private Task<bool> Eligible(string member, DateTime now, CancellationToken ct) =>
        repo.HasEligibleGymMembershipAsync(member, LocalDate(now), ct);

    private Task<Result<T>> Write<T>(Func<Task<Result<T>>> action, CancellationToken ct) =>
        repo.RunSerializableAsync(action, ct);

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, "LOCKER_VALIDATION");
    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, "LOCKER_CONFLICT", 409);
    private static Result<T> Missing<T>() => Result<T>.Failure("Không tìm thấy dữ liệu tủ.", "LOCKER_NOT_FOUND", 404);
    // MSG-44, MSG-46, MSG-47 and MSG-39 (spec v3): each keeps its own error code.
    private static Result<T> MembershipRequired<T>() => Result<T>.Failure(
        "Hội viên cần có gói Gym đang hiệu lực để thuê, gia hạn hoặc nhận tủ.", "LOCKER_MEMBERSHIP_REQUIRED", 409);
    private static Result<T> UnpaidAmount<T>(decimal amount) => Result<T>.Failure(FormattableString.Invariant(
        $"Bạn còn khoản phí tủ chưa thanh toán {amount:N0} VND. Vui lòng thanh toán tại quầy lễ tân trước khi thuê tủ mới."),
        "LOCKER_UNPAID_AMOUNT", 409);
    private static Result<T> Held<T>(string number) => Result<T>.Failure(
        $"Tủ {number} đang được giữ hoặc có hợp đồng. Hãy xử lý trước khi thay đổi tủ này.", "LOCKER_HELD", 409);
    private static Result<T> AlreadyHolding<T>() => Result<T>.Failure(
        "Mỗi hội viên chỉ được thuê tối đa 1 tủ tại một thời điểm.", "LOCKER_ALREADY_HELD", 409);
    private static Result<T> Forbidden<T>() => Result<T>.Failure("Tài khoản không có quyền thực hiện.", "LOCKER_FORBIDDEN", 403);

    private static int Months(RentalType type) => type switch
    {
        RentalType.Monthly => 1,
        RentalType.Quarterly => 3,
        RentalType.HalfYearly => 6,
        RentalType.Yearly => 12,
        _ => 0
    };

    private static int Months(LockerPricingPolicy policy) =>
        policy.RentalType == RentalType.Daily ? 0 : policy.DurationMonths ?? Months(policy.RentalType);

    private static RentalType Type(LockerRentalContract rental) =>
        rental.RentalTypeSnapshot ?? rental.LockerPricingPolicy.RentalType;

    private static DateTime RentalExpiry(LockerRentalContract rental) =>
        rental.ExpiresAt ?? Midnight(rental.EndDate.AddDays(1));

    private static int OverdueDays(LockerRentalContract rental, DateTime now)
    {
        var until = rental.ReturnedAt is { } returned && returned < now ? returned : now;
        var expiry = RentalExpiry(rental);
        return until < expiry ? 0 : LocalDate(until).DayNumber - LocalDate(expiry).DayNumber + 1;
    }

    // GBR-20: monthly after the 7-day grace period; Daily from opening time on the day after the rental day.
    private static DateTime ClearanceAt(LockerRentalContract rental)
    {
        if (Type(rental) != RentalType.Daily)
            return RentalExpiry(rental).AddDays(7);
        var gym = rental.Locker.LockerZone.Gym;
        return LocalDate(RentalExpiry(rental)).AddDays(1)
            .ToDateTime(gym.OpenTime ?? TimeOnly.MinValue, DateTimeKind.Utc).Subtract(LocalOffset);
    }

    private static decimal OverdueFees(LockerRentalContract rental, DateTime now) =>
        Type(rental) == RentalType.Daily ? 0 :
        OverdueDays(rental, now) * (rental.OverdueDailyFeeSnapshot ?? rental.LockerPricingPolicy.OverdueDailyFee);

    private static bool InUse(LockerRentalContract rental) =>
        rental.CancelledAt == null && rental.ContractStatus is
            LockerContractStatus.Active or LockerContractStatus.Overdue or LockerContractStatus.Expired;

    private static string Status(LockerRentalContract rental, DateTime now) =>
        (InUse(rental) || rental.ContractStatus == LockerContractStatus.PendingHandover && rental.ExpiresAt != null) &&
        now >= RentalExpiry(rental) ? "Overdue" :
        InUse(rental) ? "Active" : rental.ContractStatus.ToString();

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
        var rentalType = Type(rental);
        var daily = rentalType == RentalType.Daily;
        var inUse = InUse(rental);
        var hasTerm = inUse || rental.ExpiresAt != null;
        var status = Status(rental, now);
        var pendingHandover = status == "PendingHandover";
        var overdue = status == "Overdue" ? OverdueDays(rental, now) : 0;
        var allocation = inUse || rental.ContractStatus == LockerContractStatus.PendingHandover ? rental : null;
        var initial = rental.Payments.SingleOrDefault(p => p.LockerPurpose == LockerPaymentPurpose.Rental);
        var lastRenewal = rental.Payments
            .Where(p => p.LockerPurpose == LockerPaymentPurpose.Renewal && p.Order.OrderStatus == OrderStatus.Paid)
            .OrderByDescending(p => p.Id)
            .FirstOrDefault();
        var settlement = rental.Payments
            .Where(IsPaidSettlement)
            .OrderBy(p => p.Id)
            .FirstOrDefault();

        return new(
            rental.Id,
            rental.ContractNumber,
            Slot(rental.Locker, SlotStatus(rental.Locker, allocation, now)),
            rentalType.ToString(),
            status,
            rental.RentalPrice,
            DepositHeld: inUse ? rental.DepositAmount : 0,
            HandedOverAt: rental.HandedOverAt is { } handover ? Utc(handover) : null,
            ExpiresAt: hasTerm ? Utc(RentalExpiry(rental)) : null,
            CollectBy: pendingHandover && rental.CollectBy is { } collectBy ? Utc(collectBy) : null,
            OverdueDays: overdue,
            OverdueFees: overdue > 0 ? OverdueFees(rental, now) : 0,
            ClearanceAfter: overdue > 0 && rental.ReturnedAt is null ? Utc(ClearanceAt(rental)) : null,
            CanRenew: !daily && inUse && rental.ReturnedAt is null && overdue <= 7 &&
                rental.Locker.LockerStatus is not (LockerStatus.Maintenance or LockerStatus.Damaged),
            PickupInstructions: pendingHandover ? "Mang mã hợp đồng hoặc hóa đơn đến lễ tân để nhận chìa khóa." : null,
            InvoiceNumber: initial?.Invoice?.InvoiceNumber,
            OutstandingAmount: rental.OutstandingAmount,
            RenewalDue: status == "Active" && !daily && rental.ReturnedAt is null && RentalExpiry(rental) - now <= TimeSpan.FromDays(7),
            TermMonths: lastRenewal?.RenewalMonths ?? rental.TermMonthsSnapshot ?? Months(rental.LockerPricingPolicy),
            SettledAt: rental.SettledAt is { } settled ? Utc(settled) : null,
            RefundedDeposit: initial is { RefundStatus: "Refunded" } && IsDepositReturn(initial.RefundReason)
                ? initial.RefundAmount ?? 0
                : 0,
            SettlementInvoiceNumber: settlement?.Invoice?.InvoiceNumber,
            ReplacementLockerId: rental.ReplacementLockerId,
            StartDate: hasTerm ? rental.StartDate : null,
            LastRentalDate: hasTerm ? rental.EndDate : null,
            DailyKeyOutstanding: status == "Overdue" && daily && rental.ReturnedAt is null && rental.KeyLossConfirmedAt is null,
            ReturnedAt: rental.ReturnedAt is { } returned ? Utc(returned) : null,
            ReminderSentAt: rental.ReminderSentAt is { } reminder ? Utc(reminder) : null,
            OverdueNoticeSentAt: rental.FinalNoticeAt is { } notice ? Utc(notice) : null,
            OutstandingPaidAt: rental.OutstandingPaidAt is { } paidAt ? Utc(paidAt) : null,
            KeyDeposit: rental.DepositAmount,
            PendingOrderNumber: rental.Payments.Where(p => Pending(p, now) && p.LockerPurpose is LockerPaymentPurpose.Rental or LockerPaymentPurpose.Renewal)
                .OrderByDescending(p => p.Id).Select(p => p.Order.OrderNumber).FirstOrDefault());
    }

    private static LockerPolicy Policy(LockerPricingPolicy p) =>
        new(p.Id, p.PolicyName, p.RentalType.ToString(), Months(p),
            p.RentalType == RentalType.Daily ? 0 : p.RentalPrice, p.DepositAmount,
            p.OverdueDailyFee);

    private void Audit(string? actor, LockerRentalContract rental, string value, DateTime now) =>
        repo.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = nameof(LockerRentalContract),
            EntityId = rental.Id,
            NewValue = value,
            OccurredAt = now
        });

    private void Notify(LockerRentalContract rental, string title, string message, DateTime now) =>
        repo.Add(new Notification
        {
            RecipientUserId = rental.CustomerUserId,
            NotificationType = "Locker",
            Title = title,
            Message = message,
            EntityType = nameof(LockerRentalContract),
            EntityId = rental.Id,
            NotifiedAt = now
        });

    public async Task<Result<IReadOnlyList<LockerSlot>>> Grid(string actor, UserRole role, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<IReadOnlyList<LockerSlot>>();
        var now = Now.UtcDateTime;
        var lockers = await repo.GetGymLockersAsync(user.GymId, ct);
        var rentals = await repo.GetGymAllocationsAsync(user.GymId, now, ct);
        var byLocker = rentals.ToLookup(r => r.LockerId);
        var replacements = rentals.Where(r => r.ReplacementLockerId != null).Select(r => r.ReplacementLockerId!.Value).ToHashSet();
        return Result<IReadOnlyList<LockerSlot>>.Success(lockers.Select(l =>
            Slot(l, replacements.Contains(l.Id) ? "Reserved" : SlotStatus(l, byLocker[l.Id].FirstOrDefault(), now))).ToArray());
    }

    public async Task<Result<IReadOnlyList<LockerPolicy>>> Pricing(string actor, UserRole role, CancellationToken ct)
    {
        if (await Actor(actor, role, ct) is null)
            return Forbidden<IReadOnlyList<LockerPolicy>>();
        var policies = (await repo.GetActivePoliciesAsync(ct))
            .Where(p => role != UserRole.Member || p.RentalType != RentalType.Daily);
        return Result<IReadOnlyList<LockerPolicy>>.Success(policies.Select(Policy).OrderBy(p => p.Months).ToArray());
    }

    public async Task<Result<LockerRentalView?>> MyRental(string actor, CancellationToken ct)
    {
        if (await Actor(actor, UserRole.Member, ct) is null)
            return Forbidden<LockerRentalView?>();
        var now = Now.UtcDateTime;
        var rental = await repo.GetCurrentRentalAsync(actor, now, ct);
        return Result<LockerRentalView?>.Success(rental is null ? null : View(rental, now));
    }

    public async Task<Result<PagedResponse<LockerRentalView>>> MyHistory(string actor, int page, CancellationToken ct)
    {
        if (await Actor(actor, UserRole.Member, ct) is null)
            return Forbidden<PagedResponse<LockerRentalView>>();
        if (page < 1)
            return Invalid<PagedResponse<LockerRentalView>>("Số trang phải lớn hơn hoặc bằng 1.");
        var history = await repo.GetRentalHistoryAsync(actor, page, ct);
        var now = Now.UtcDateTime;
        return Result<PagedResponse<LockerRentalView>>.Success(new(history.Page, history.PageSize,
            history.TotalCount, history.Items.Select(r => View(r, now)).ToArray()));
    }

    public async Task<Result<LockerRentalView>> MyRentalDetails(string actor, int id, CancellationToken ct)
    {
        var member = await Actor(actor, UserRole.Member, ct);
        if (member is null)
            return Forbidden<LockerRentalView>();
        var rental = await repo.GetRentalAsync(id, member.GymId, actor, ct);
        return rental is null ? Missing<LockerRentalView>() : Result<LockerRentalView>.Success(View(rental, Now.UtcDateTime));
    }
}
