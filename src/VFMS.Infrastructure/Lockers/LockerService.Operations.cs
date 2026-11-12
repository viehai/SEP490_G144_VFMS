using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Payments;

namespace VFMS.Infrastructure.Lockers;

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
            !await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.Id != rental.Id &&
                (r.LockerId == rental.LockerId || r.ReplacementLockerId == rental.LockerId), ct))
            rental.Locker.LockerStatus = LockerStatus.Available;
    }

    public async Task Expire(CancellationToken ct)
    {
        var now = Now.UtcDateTime;
        var result = await Write(async () =>
        {
            var stale = await db.PaymentTransactions.Include(p => p.LockerRentalContract!).ThenInclude(r => r.Locker)
                .Include(p => p.Order).Where(p => p.LockerRentalContractId != null && p.HoldExpiresAt <= now &&
                    (p.Order.OrderStatus == OrderStatus.Pending ||
                    p.LockerPurpose == LockerPaymentPurpose.Rental && p.LockerRentalContract!.ContractStatus == LockerContractStatus.PendingPayment &&
                    p.Order.OrderStatus == OrderStatus.Expired)).OrderBy(p => p.Id).Take(100).ToListAsync(ct);
            foreach (var charge in stale)
            {
                charge.Order.OrderStatus = OrderStatus.Expired;
                charge.PaymentStatus = PaymentStatus.Cancelled;
                await ReleaseInitialHold(charge, now, ct);
            }
            var expiredRelocations = await Rentals.Where(r => r.ReplacementLockerId != null && r.ExpiresAt <= now)
                .OrderBy(r => r.Id).Take(100).ToListAsync(ct);
            foreach (var rental in expiredRelocations)
            {
                ReleaseReplacement(rental);
                Audit(null, rental, "LockerReplacementExpired", now);
            }
            var paidDaily = await Rentals.Where(r => r.ContractStatus == LockerContractStatus.PendingHandover &&
                (r.RentalTypeSnapshot == RentalType.Daily ||
                 r.RentalTypeSnapshot == null && r.LockerPricingPolicy.RentalType == RentalType.Daily))
                .OrderBy(r => r.Id).Take(100).ToListAsync(ct);
            foreach (var rental in paidDaily)
            {
                var charge = rental.Payments.Single(p => p.LockerPurpose == LockerPaymentPurpose.Rental);
                if (charge.PaidAt is not { } paidAt || rental.Locker.LockerZone.Gym.CloseTime is not { } close ||
                    now < Midnight(LocalDate(paidAt)).Add(close.ToTimeSpan()))
                    continue;
                rental.ContractStatus = LockerContractStatus.Cancelled;
                rental.CancelledAt = now;
                await ReleaseLocker(rental, now, ct);
                CheckoutRefunds.Request(db, charge, "DailyKeyNotHandedOver", now);
                Audit(null, rental, "LockerDailyHandoverExpired:RefundRequested", now);
            }
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.ErrorCode);
    }

    public Task<Result<LockerRentalView>> SendNotice(string actor, int id, CancellationToken ct) => Write(async () =>
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<LockerRentalView>();
        var rental = await StaffRental(staff, id, ct);
        if (rental is null)
            return Missing<LockerRentalView>();
        var now = Now.UtcDateTime;
        if (Status(rental, now) != "Overdue" || Type(rental) == RentalType.Daily || OverdueDays(rental, now) > 7)
            return Conflict<LockerRentalView>("Chỉ gửi thông báo cuối cho hợp đồng quá hạn từ 1 đến 7 ngày.");
        if (rental.FinalNoticeAt is null)
        {
            rental.FinalNoticeAt = now;
            Notify(rental, "Thông báo quá hạn thuê tủ", NoticeText(rental, now), now);
            Audit(actor, rental, "LockerFinalNoticeQueued", now);
            await db.SaveChangesAsync(ct);
        }
        return Result<LockerRentalView>.Success(View(rental, now));
    }, ct);

    private static string NoticeText(LockerRentalContract rental, DateTime now) => FormattableString.Invariant(
        $"Tủ {rental.Locker.LockerNumber} đã quá hạn {OverdueDays(rental, now)} ngày. Phí hiện tại {OverdueFees(rental, now):N0} VND. Hạn xử lý {Local(ClearanceAt(rental)):dd/MM/yyyy HH:mm}.");

    public async Task SendPendingNoticeEmail(IEmailService email, CancellationToken ct)
    {
        var now = Now.UtcDateTime;
        var claimed = await Write(async () =>
        {
            var rental = await Rentals.Where(r => r.FinalNoticeAt != null && r.FinalNoticeEmailSentAt == null &&
                (r.FinalNoticeRetryAt == null || r.FinalNoticeRetryAt <= now)).OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
            if (rental is not null)
            {
                rental.FinalNoticeRetryAt = now.AddMinutes(2);
                await db.SaveChangesAsync(ct);
            }
            return Result<LockerRentalContract?>.Success(rental);
        }, ct);
        if (claimed.Data is not { } delivery)
            return;
        await email.SendAsync(delivery.Customer.Email, "Thông báo quá hạn thuê tủ",
            NoticeText(delivery, delivery.FinalNoticeAt!.Value), ct);
        var sent = Now.UtcDateTime;
        await db.LockerRentalContracts.Where(r => r.Id == delivery.Id && r.FinalNoticeAt == delivery.FinalNoticeAt &&
            r.FinalNoticeRetryAt == delivery.FinalNoticeRetryAt)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.FinalNoticeEmailSentAt, sent), ct);
    }

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
            if (Status(rental, now) != "Overdue" || Type(rental) == RentalType.Daily || now < ClearanceAt(rental))
                return Conflict<int>("Chỉ xử lý thuê tháng sau khi hết đủ 7 ngày quá hạn.");
            if (request.Inventory is null || request.Inventory.Length > 100 ||
                request.NoBelongings != (request.Inventory.Length == 0) ||
                request.Inventory.Any(i => i is null || string.IsNullOrWhiteSpace(i.Name) ||
                    i.Name.Length > 200 || i.Quantity is < 1 or > 10000))
                return Invalid<int>("Nhập danh sách đồ kiểm kê hoặc xác nhận tủ trống.");
            var existing = await db.LockerIncidentLogs.Where(i => i.LockerRentalContractId == id && i.VerifiedAt == null)
                .Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
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
            db.LockerIncidentLogs.Add(incident);
            Audit(actor, rental, "LockerClearanceSubmitted", now);
            await db.SaveChangesAsync(ct);
            return Result<int>.Success(incident.Id, 201);
        }, ct);

    public async Task<Result<IReadOnlyList<LockerClearanceView>>> Clearances(string actor, CancellationToken ct)
    {
        var manager = await Actor(actor, UserRole.Manager, ct);
        if (manager is null)
            return Forbidden<IReadOnlyList<LockerClearanceView>>();
        var data = await db.LockerIncidentLogs.AsNoTracking().Where(i => i.LockerRentalContractId != null &&
                i.VerifiedAt == null && i.Locker.LockerZone.GymId == manager.GymId &&
                (i.LockerRentalContract!.ContractStatus == LockerContractStatus.Active ||
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.Overdue ||
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.Expired ||
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.PendingHandover))
            .OrderBy(i => i.Id).Take(100).ToListAsync(ct);
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
            var incident = await db.LockerIncidentLogs.SingleOrDefaultAsync(i => i.Id == incidentId &&
                i.LockerRentalContractId != null && i.Locker.LockerZone.GymId == manager.GymId, ct);
            if (incident?.LockerRentalContractId is not { } rentalId)
                return Missing<LockerRentalView>();
            if (incident.HandledByUserId == actor)
                return Forbidden<LockerRentalView>();
            var rental = (await StaffRental(manager, rentalId, ct))!;
            var now = Now.UtcDateTime;
            if (incident.VerifiedAt is not null)
                return Result<LockerRentalView>.Success(View(rental, now));
            if (Status(rental, now) != "Overdue" || Type(rental) == RentalType.Daily || now < ClearanceAt(rental) ||
                rental.Payments.Any(p => Pending(p, now) ||
                    p.LockerPurpose == LockerPaymentPurpose.Settlement && p.Order.OrderStatus == OrderStatus.Paid))
                return Conflict<LockerRentalView>("Hợp đồng đã thay đổi hoặc còn giao dịch phải xử lý.");

            rental.OutstandingAmount = OverdueFees(rental, now) +
                (rental.KeyReplacementFeeSnapshot ?? rental.LockerPricingPolicy.KeyReplacementFee);
            rental.FineAmount = rental.OutstandingAmount;
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
            await db.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    public Task<Result<LockerSlot>> SetMaintenance(string actor, UserRole role, int lockerId, LockerMaintenanceRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var staff = await Actor(actor, role, ct);
        if (staff is null)
            return Forbidden<LockerSlot>();
        var locker = await db.Lockers.Include(l => l.LockerZone)
            .SingleOrDefaultAsync(l => l.Id == lockerId && l.LockerZone.GymId == staff.GymId, ct);
        if (locker is null)
            return Missing<LockerSlot>();
        var now = Now.UtcDateTime;
        var allocation = await Allocations(Rentals, now).SingleOrDefaultAsync(r =>
            r.LockerId == lockerId || r.ReplacementLockerId == lockerId, ct);
        if (allocation is not null)
        {
            if (!request.Maintenance || allocation.LockerId != lockerId || !InUse(allocation) || OverdueDays(allocation, now) > 0)
                return Conflict<LockerSlot>("Cần xử lý lượt giữ hoặc hợp đồng hiện tại trước khi đổi trạng thái bảo trì.");
            if (locker.LockerStatus != LockerStatus.Maintenance)
                Notify(allocation, "Tủ đang thuê cần bảo trì", "Vui lòng chọn một tủ còn trống để chuyển hợp đồng hiện tại.", now);
        }
        if (!request.Maintenance && (!request.SafeInspection ||
            locker.LockerStatus is not (LockerStatus.Maintenance or LockerStatus.Damaged or LockerStatus.Available)))
            return Invalid<LockerSlot>("Cần xác nhận khóa đã được kiểm tra an toàn.");
        locker.LockerStatus = request.Maintenance ? LockerStatus.Maintenance : LockerStatus.Available;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = nameof(Locker),
            EntityId = lockerId,
            NewValue = locker.LockerStatus.ToString(),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Result<LockerSlot>.Success(Slot(locker, SlotStatus(locker, null, now)));
    }, ct);
}
