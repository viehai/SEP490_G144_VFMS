using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class LockerService
{
    private async Task ReleaseInitialHold(PaymentTransaction charge, DateTime now, CancellationToken ct)
    {
        var rental = charge.LockerRentalContract!;
        if (charge.LockerPurpose != LockerPaymentPurpose.Rental || rental.ContractStatus != LockerContractStatus.PendingPayment)
            return;
        rental.ContractStatus = LockerContractStatus.Cancelled;
        rental.CancelledAt = now;
        await ReleaseLocker(rental, now, ct);
    }

    private async Task ReleaseLocker(LockerRentalContract rental, DateTime now, CancellationToken ct)
    {
        if (rental.Locker.LockerStatus == LockerStatus.Reserved &&
            !await repo.HasAllocationAsync(now, rental.LockerId, null, rental.Id, ct))
            rental.Locker.LockerStatus = LockerStatus.Available;
    }

    public async Task Expire(CancellationToken ct)
    {
        var now = Now.UtcDateTime;
        var result = await Write(async () =>
        {
            var stale = await repo.GetStaleChargesAsync(now, 100, ct);
            foreach (var charge in stale)
            {
                charge.Order.OrderStatus = OrderStatus.Expired;
                charge.PaymentStatus = PaymentStatus.Cancelled;
                ReleaseVoucher(charge);
                await ReleaseInitialHold(charge, now, ct);
            }
            var expiredRelocations = await repo.GetExpiredReplacementsAsync(now, 100, ct);
            foreach (var rental in expiredRelocations)
            {
                ReleaseReplacement(rental);
                Audit(null, rental, "LockerReplacementExpired", now);
            }

            // GBR-106: a Daily key is collected on the paid day, an online monthly key by the pickup deadline.
            foreach (var rental in await repo.GetAwaitingHandoverAsync(100, ct))
            {
                var charge = rental.Payments.SingleOrDefault(p => p.LockerPurpose == LockerPaymentPurpose.Rental);
                var deadline = Type(rental) == RentalType.Daily
                    ? charge?.PaidAt is { } paidAt ? AtClose(LocalDate(paidAt), rental.Locker.LockerZone.Gym) : (DateTime?)null
                    : rental.CollectBy;
                if (charge is null || deadline is null || now < deadline)
                    continue;
                rental.ContractStatus = LockerContractStatus.Cancelled;
                rental.CancelledAt = now;
                rental.CollectBy = null;
                await ReleaseLocker(rental, now, ct);
                // GBR-102: the payment becomes an Unapplied Transaction that the front desk resolves.
                repo.RequestRefund(charge, "KeyNotCollected", now);
                Audit(null, rental, "LockerHandoverExpired:UnappliedTransaction", now);
            }

            // GBR-20: one in-app reminder 7 days before expiry and one overdue notice on overdue day 5.
            foreach (var rental in await repo.GetDueRemindersAsync(now, 100, ct))
            {
                rental.ReminderSentAt = now;
                Notify(rental, "Tủ sắp hết hạn", FormattableString.Invariant(
                    $"Tủ {rental.Locker.LockerNumber} hết hạn vào {Local(RentalExpiry(rental).AddDays(-1)):dd/MM/yyyy}. Hãy gia hạn để tiếp tục sử dụng."), now);
            }
            foreach (var rental in await repo.GetDueOverdueNoticesAsync(now, 100, ct))
            {
                rental.FinalNoticeAt = now;
                Notify(rental, "Thông báo quá hạn thuê tủ", NoticeText(rental, now), now);
            }
            await repo.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.ErrorCode);
    }

    private static string NoticeText(LockerRentalContract rental, DateTime now) => FormattableString.Invariant(
        $"Tủ {rental.Locker.LockerNumber} đã quá hạn {OverdueDays(rental, now)} ngày. Phí hiện tại {OverdueFees(rental, now):N0} VND. Hạn xử lý {Local(ClearanceAt(rental)):dd/MM/yyyy HH:mm}.");

    public Task<Result<int>> RequestClearance(string actor, int id, LockerClearanceRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<int>();
            var rental = await StaffRental(staff, id, ct);
            if (rental is null)
                return Missing<int>();
            var now = Now.UtcDateTime;
            if (rental.ReturnedAt is not null || Status(rental, now) != "Overdue" || now < ClearanceAt(rental))
                return Conflict<int>(Type(rental) == RentalType.Daily
                    ? "Chỉ xử lý thuê ngày từ giờ mở cửa của ngày hôm sau."
                    : "Chỉ xử lý thuê tháng sau khi hết đủ 7 ngày quá hạn.");
            if (request.Inventory is null || request.Inventory.Length > 100 ||
                request.NoBelongings != (request.Inventory.Length == 0) ||
                request.Inventory.Any(i => i is null || string.IsNullOrWhiteSpace(i.Name) ||
                    i.Name.Length > 200 || i.Quantity is < 1 or > 10000))
                return Invalid<int>("Nhập danh sách đồ kiểm kê hoặc xác nhận tủ trống.");
            var existing = await repo.GetOpenClearanceIdAsync(id, ct);
            if (existing is { } existingId)
                return Result<int>.Success(existingId);
            var incident = new LockerIncidentLog
            {
                LockerId = rental.LockerId,
                LockerRentalContractId = id,
                MemberUserId = rental.CustomerUserId,
                HandledByUserId = actor,
                IncidentType = LockerIncidentType.Other,
                ReportedAt = now,
                Description = JsonSerializer.Serialize(request.Inventory)
            };
            repo.Add(incident);
            Audit(actor, rental, "LockerClearanceSubmitted", now);
            await repo.SaveChangesAsync(ct);
            return Result<int>.Success(incident.Id, 201);
        }, ct);

    public async Task<Result<IReadOnlyList<LockerClearanceView>>> Clearances(string actor, CancellationToken ct)
    {
        var manager = await Actor(actor, UserRole.Manager, ct);
        if (manager is null)
            return Forbidden<IReadOnlyList<LockerClearanceView>>();
        var data = await repo.GetOpenClearancesAsync(manager.GymId, 100, ct);
        return Result<IReadOnlyList<LockerClearanceView>>.Success(data.Select(i => new LockerClearanceView(
            i.Id, i.LockerRentalContractId!.Value, i.HandledByUserId, Utc(i.ReportedAt),
            JsonSerializer.Deserialize<LockerInventoryItem[]>(i.Description) ?? [])).ToArray());
    }

    public Task<Result<LockerRentalView>> VerifyClearance(string actor, int incidentId, CancellationToken ct) =>
        Write(async () =>
        {
            var manager = await Actor(actor, UserRole.Manager, ct);
            if (manager is null)
                return Forbidden<LockerRentalView>();
            var incident = await repo.GetClearanceAsync(incidentId, manager.GymId, ct);
            if (incident?.LockerRentalContractId is not { } rentalId)
                return Missing<LockerRentalView>();
            if (incident.HandledByUserId == actor)
                return Forbidden<LockerRentalView>();
            var rental = (await StaffRental(manager, rentalId, ct))!;
            var now = Now.UtcDateTime;
            if (incident.VerifiedAt is not null)
                return Result<LockerRentalView>.Success(View(rental, now));
            if (rental.ReturnedAt is not null || Status(rental, now) != "Overdue" || now < ClearanceAt(rental) ||
                rental.Payments.Any(p => Pending(p, now) || IsPaidSettlement(p)))
                return Conflict<LockerRentalView>("Hợp đồng đã thay đổi hoặc còn giao dịch phải xử lý.");

            // GBR-20: the deposit is forfeited; a monthly overdue fee (capped at 7 days) becomes an unpaid amount (GBR-110).
            // A Daily rental has no overdue fee, so nothing is owed.
            var owed = Type(rental) == RentalType.Daily ? 0 :
                Math.Min(OverdueDays(rental, now), 7) * (rental.OverdueDailyFeeSnapshot ?? rental.LockerPricingPolicy.OverdueDailyFee);
            rental.OutstandingAmount = owed > 0 ? owed : null;
            rental.FineAmount = owed + rental.DepositAmount;
            ReleaseReplacement(rental);
            rental.SettledAt = now;
            rental.SettledByUserId = actor;
            rental.CancelledAt = now;
            rental.ContractStatus = LockerContractStatus.Abandoned;
            rental.Locker.LockerStatus = LockerStatus.Maintenance;
            incident.VerifiedByUserId = actor;
            incident.VerifiedAt = now;
            incident.FineAmount = rental.FineAmount;
            Audit(actor, rental, "LockerClearanceVerified", now);
            Notify(rental, "Đã xử lý tủ quá hạn", "Liên hệ lễ tân để nhận lại đồ đã kiểm kê và xem khoản phí.", now);
            await repo.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    public Task<Result<LockerSlot>> SetMaintenance(string actor, UserRole role, int lockerId, LockerMaintenanceRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var staff = await Actor(actor, role, ct);
        if (staff is null)
            return Forbidden<LockerSlot>();
        var locker = await repo.GetGymLockerAsync(lockerId, staff.GymId, ct);
        if (locker is null || locker.LockerStatus == LockerStatus.Inactive)
            return Missing<LockerSlot>();
        var now = Now.UtcDateTime;
        var allocation = await repo.GetAllocationForLockerAsync(lockerId, now, ct);
        if (allocation is not null)
        {
            // GBR-111: only an Active contract that is not overdue may move to a replacement locker.
            if (!request.Maintenance || allocation.LockerId != lockerId || !InUse(allocation) || OverdueDays(allocation, now) > 0)
                return Held<LockerSlot>(locker.LockerNumber);
            if (locker.LockerStatus != LockerStatus.Maintenance)
                Notify(allocation, "Tủ đang thuê cần bảo trì", "Vui lòng chọn một tủ còn trống để chuyển hợp đồng hiện tại.", now);
        }
        if (!request.Maintenance && (!request.SafeInspection ||
            locker.LockerStatus is not (LockerStatus.Maintenance or LockerStatus.Damaged or LockerStatus.Available)))
            return Invalid<LockerSlot>("Cần xác nhận khóa đã được kiểm tra an toàn.");
        locker.LockerStatus = request.Maintenance ? LockerStatus.Maintenance : LockerStatus.Available;
        repo.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = nameof(Locker),
            EntityId = lockerId,
            NewValue = locker.LockerStatus.ToString(),
            OccurredAt = now
        });
        await repo.SaveChangesAsync(ct);
        return Result<LockerSlot>.Success(Slot(locker, SlotStatus(locker, null, now)));
    }, ct);

    public async Task<Result<LockerOutstanding>> MyOutstanding(string actor, CancellationToken ct) =>
        await Actor(actor, UserRole.Member, ct) is null
            ? Forbidden<LockerOutstanding>()
            : Result<LockerOutstanding>.Success(new(await repo.GetUnpaidAmountAsync(actor, ct)));

    public async Task<Result<IReadOnlyList<LockerStaffRental>>> Unpaid(string actor, string? search, CancellationToken ct)
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<IReadOnlyList<LockerStaffRental>>();
        search = search?.Trim();
        if (search?.Length > 100)
            return Invalid<IReadOnlyList<LockerStaffRental>>("Từ khóa tìm kiếm tối đa 100 ký tự.");
        var now = Now.UtcDateTime;
        var data = await repo.GetUnpaidRentalsAsync(staff.GymId, search, ct);
        return Result<IReadOnlyList<LockerStaffRental>>.Success(data.Select(r =>
            new LockerStaffRental(View(r, now), r.CustomerUserId, r.Customer.FullName, r.Customer.PhoneNumber)).ToArray());
    }

    // UC 5.3 Step 3: Staff collects the unpaid amount in cash and releases the belongings; this lifts the rental block (GBR-110).
    public Task<Result<LockerRentalView>> CollectOutstanding(string actor, int id, decimal amount, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerRentalView>();
            var rental = await StaffRental(staff, id, ct);
            if (rental is null)
                return Missing<LockerRentalView>();
            var now = Now.UtcDateTime;
            if (rental.ContractStatus != LockerContractStatus.Abandoned || rental.OutstandingAmount is not > 0)
                return Conflict<LockerRentalView>("Hợp đồng không có khoản phí cần thu.");
            if (rental.OutstandingPaidAt is not null)
                return Result<LockerRentalView>.Success(View(rental, now));
            if (amount != rental.OutstandingAmount)
                return Conflict<LockerRentalView>(FormattableString.Invariant(
                    $"Số tiền thu phải đúng {rental.OutstandingAmount:N0} VND."));
            rental.OutstandingPaidAt = now;
            Audit(actor, rental, "LockerOutstandingCollected", now);
            Notify(rental, "Đã thu phí tủ", "Khoản phí tủ quá hạn đã được thanh toán tại quầy.", now);
            await repo.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);
}
