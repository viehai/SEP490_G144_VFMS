using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService
{
    private async Task ReleaseInitialHold(LockerRentalPayment charge, DateTime now, CancellationToken ct)
    {
        var rental = charge.Rental;
        if (charge.Purpose != LockerPaymentPurpose.Rental || rental.ContractStatus != LockerContractStatus.PendingPayment)
            return;
        rental.ContractStatus = LockerContractStatus.Cancelled;
        rental.CancelledAt = now;
        await ReleaseLocker(rental, now, ct);
    }

    private async Task ReleaseLocker(LockerRentalContract rental, DateTime now, CancellationToken ct)
    {
        if (rental.Locker.LockerStatus == LockerStatus.Reserved &&
            !await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.Id != rental.Id && r.LockerId == rental.LockerId, ct))
            rental.Locker.LockerStatus = LockerStatus.Available;
    }

    public async Task Expire(CancellationToken ct)
    {
        var now = Now.UtcDateTime;
        var result = await Write(async () =>
        {
            var stale = await db.LockerRentalPayments.Include(p => p.Rental).ThenInclude(r => r.Locker)
                .Include(p => p.Order).ThenInclude(o => o.PaymentTransaction).Where(p => p.HoldExpiresAt <= now &&
                    (p.Order.OrderStatus == OrderStatus.Pending ||
                    p.Purpose == LockerPaymentPurpose.Rental && p.Rental.ContractStatus == LockerContractStatus.PendingPayment &&
                    p.Order.OrderStatus == OrderStatus.Expired)).OrderBy(p => p.Id).Take(100).ToListAsync(ct);
            foreach (var charge in stale)
            {
                charge.Order.OrderStatus = OrderStatus.Expired;
                charge.Order.PaymentTransaction!.PaymentStatus = PaymentStatus.Cancelled;
                await ReleaseInitialHold(charge, now, ct);
            }
            var uncollected = await Rentals.Where(r => r.ContractStatus == LockerContractStatus.PendingHandover &&
                r.CollectBy <= now).OrderBy(r => r.Id).Take(100).ToListAsync(ct);
            foreach (var rental in uncollected)
            {
                var initial = rental.Payments.Single(p => p.Purpose == LockerPaymentPurpose.Rental);
                initial.RefundAmount = initial.Order.PaymentTransaction!.AmountReceived;
                initial.RefundReason = "KeyNotCollected";
                rental.ContractStatus = LockerContractStatus.Cancelled;
                rental.CancelledAt = now;
                await ReleaseLocker(rental, now, ct);
                Audit(null, rental, "LockerPickupExpired:RefundRequested", now);
                Notify(rental, "Đã hết hạn nhận chìa tủ", "Hệ thống đã tạo yêu cầu hoàn toàn bộ khoản thanh toán.", now);
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
        if (!InUse(rental) || OverdueDays(rental, now) is < 1 or > 7)
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
        $"Tủ {rental.Locker.LockerNumber} đã quá hạn {OverdueDays(rental, now)} ngày. Phí hiện tại {OverdueFees(rental, now):N0} VND. Hạn xử lý {Local(RentalExpiry(rental).AddDays(7)):dd/MM/yyyy HH:mm}.");

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
            if (!InUse(rental) || OverdueDays(rental, now) <= 7)
                return Conflict<int>("Chỉ cưỡng chế sau hơn 7 ngày quá hạn.");
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
                LockerId = rental.LockerId, LockerRentalContractId = id, MemberUserId = rental.CustomerUserId,
                HandledByUserId = actor, IncidentType = LockerIncidentType.Other, ReportedAt = now,
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
                 i.LockerRentalContract.ContractStatus == LockerContractStatus.Expired))
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
            if (!InUse(rental) || OverdueDays(rental, now) <= 7 ||
                rental.Payments.Any(p => Pending(p, now) ||
                    p.Purpose == LockerPaymentPurpose.Settlement && p.Order.OrderStatus == OrderStatus.Paid))
                return Conflict<LockerRentalView>("Hợp đồng đã thay đổi hoặc còn giao dịch phải xử lý.");

            if (Type(rental) == RentalType.Daily)
                rental.FineAmount = rental.DepositAmount;
            else
            {
                rental.OutstandingAmount = OverdueFees(rental, now) +
                    (rental.KeyReplacementFeeSnapshot ?? rental.LockerPricingPolicy.KeyReplacementFee);
                rental.FineAmount = rental.OutstandingAmount;
            }
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
        if (await Allocations(db.LockerRentalContracts, now).AnyAsync(r => r.LockerId == lockerId, ct))
            return Conflict<LockerSlot>("Cần xử lý hợp đồng hiện tại trước khi đổi trạng thái bảo trì.");
        if (!request.Maintenance && (!request.SafeInspection ||
            locker.LockerStatus is not (LockerStatus.Maintenance or LockerStatus.Damaged or LockerStatus.Available)))
            return Invalid<LockerSlot>("Cần xác nhận khóa đã được kiểm tra an toàn.");
        locker.LockerStatus = request.Maintenance ? LockerStatus.Maintenance : LockerStatus.Available;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor, ActionType = AuditActionType.Update, AuditEntity = nameof(Locker), EntityId = lockerId,
            NewValue = locker.LockerStatus.ToString(), OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Result<LockerSlot>.Success(Slot(locker, SlotStatus(locker, null, now)));
    }, ct);
}
