using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.PersonalTraining;

public sealed partial class PersonalTrainingService
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(15);
    private sealed record Booking(PTBookingPreview Preview, Package Package, TrainerProfile Trainer,
        MemberMembership Membership, PTAvailability[] Pattern);

    private async Task<Result<Booking>> BuildBooking(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var now = Now;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        if (request.StartDate < today || request.StartDate.DayNumber > DateOnly.MaxValue.DayNumber - 180 ||
            request.TemplateIds.Distinct().Count() != request.TemplateIds.Length)
            return Invalid<Booking>("Ngày bắt đầu hoặc mẫu lịch không hợp lệ.");
        var member = await User(actor, UserRole.Member, ct);
        if (member is null)
            return Forbidden<Booking>();
        var trainer = await User(request.TrainerId, UserRole.PT, ct);
        if (trainer is null || trainer.GymId != member.GymId || actor == request.TrainerId)
            return Missing<Booking>();
        var profile = await db.TrainerProfiles.SingleOrDefaultAsync(p => p.PTUserId == request.TrainerId && p.IsActive, ct);
        var package = await PTPackages.SingleOrDefaultAsync(p => p.Id == request.PackageId &&
            p.PackageStatus == PackageStatus.Active, ct);
        if (profile is null || package is null || package.Components.Single().ApplicableTrainerLevel != profile.Level)
            return Invalid<Booking>("Gói PT hoặc cấp độ huấn luyện viên không hợp lệ.");
        var terms = package.Components.Single();
        var end = request.StartDate.AddDays(terms.DurationDays!.Value - 1);
        var pattern = await Templates(request.TrainerId).Where(x => request.TemplateIds.Contains(x.Id))
            .OrderBy(x => x.DayOfWeek).ToArrayAsync(ct);
        if (pattern.Length != request.TemplateIds.Length || pattern.Select(x => x.DayOfWeek).Distinct().Count() != pattern.Length)
            return Invalid<Booking>("Chọn một ca đã công bố cho mỗi ngày tập.");
        var dates = PersonalTrainingRules.Occurrences(request.StartDate, end, pattern, terms.SessionCount!.Value)
            .Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.EndTime, "Available")).ToArray();
        if (dates.Length != terms.SessionCount || dates.Any(s => !InHours(trainer.Gym, s.StartTime, s.EndTime) ||
                Utc(s.Date, s.StartTime) <= now.Add(HoldDuration)))
            return Conflict<Booking>("Không đủ lịch trong thời hạn gói PT hoặc có ca ngoài giờ hoạt động.");
        var lastDate = dates[^1].Date;
        var membership = await Coverage(actor).Where(m => m.StartDate <= today && m.EndDate >= lastDate)
            .OrderByDescending(m => m.EndDate).FirstOrDefaultAsync(ct);
        if (membership is null)
            return Result<Booking>.Failure("Cần gói Gym đang hoạt động và bao phủ toàn bộ ngày tập.", "PT_GYM_COVERAGE", 409);
        if (await HasConflict(request.TrainerId, actor, dates, now, ct))
            return Conflict<Booking>("Lịch dự kiến trùng lịch của PT, hội viên hoặc Yoga.");
        return Result<Booking>.Success(new(new(package.Id, request.TrainerId, end, package.Price, dates),
            package, profile, membership, pattern));
    }

    public async Task<Result<PTBookingPreview>> Preview(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var booking = await BuildBooking(actor, request, ct);
        return booking.IsSuccess ? Result<PTBookingPreview>.Success(booking.Data!.Preview) :
            Result<PTBookingPreview>.Failure(booking.ErrorMessage!, booking.ErrorCode!, booking.StatusCode);
    }

    public Task<Result<PTCheckoutResponse>> Checkout(string actor, PTBookingRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            await payments.LockUsersAsync([actor, request.TrainerId], ct);
            var result = await BuildBooking(actor, request, ct);
            if (!result.IsSuccess)
                return Result<PTCheckoutResponse>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
            var booking = result.Data!;
            var component = booking.Package.Components.Single();
            var now = Now;
            var order = MembershipPurchaseService.NewOrder(actor, Today, OrderChannel.Online);
            order.Items.Add(MembershipPurchaseService.NewOrderItem(booking.Package, actor, request.StartDate));

            var subtotal = booking.Package.Price;
            if (!string.IsNullOrWhiteSpace(request.VoucherCode))
            {
                var voucher = await ReserveVoucher(actor, request.VoucherCode, subtotal, now, ct);
                if (voucher is null)
                    return Result<PTCheckoutResponse>.Failure("Voucher không hợp lệ hoặc đã hết lượt.", "PT_VOUCHER_INVALID");
                order.VoucherRedemption = voucher;
            }
            var due = subtotal - (order.VoucherRedemption?.DiscountAmount ?? 0);
            var qr = gateway.CreateVietQr(order.OrderNumber, due);
            if (qr is null)
                return Result<PTCheckoutResponse>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);

            order.PaymentTransaction = new PaymentTransaction
            {
                UserId = actor, GatewayName = "SePay", PaymentMethod = PaymentMethod.VietQR, AmountDue = due
            };
            var purchase = new PTPurchase
            {
                Order = order, MemberMembershipId = booking.Membership.Id, MemberUserId = actor,
                PTUserId = request.TrainerId, Status = PTPurchaseStatus.PendingPayment,
                HoldExpiresAt = now.Add(HoldDuration), PackageNameSnapshot = booking.Package.Name,
                TrainerLevelSnapshot = booking.Trainer.Level, PurchaseDate = Today,
                FreezeMinimumDaysSnapshot = component.PTFreezeMinimumDays ?? 0,
                FreezeMaxTotalDaysSnapshot = component.PTFreezeMaxTotalDays ?? 0,
                StartDate = request.StartDate, EndDate = booking.Preview.ExpiresOn,
                PurchasePrice = due, TotalSessions = booking.Preview.Sessions.Count,
                RemainingSessions = booking.Preview.Sessions.Count,
                WeeklyPatternJson = JsonSerializer.Serialize(booking.Pattern.Select(p => new PTSlotInput(p.DayOfWeek!.Value, p.StartTime))),
                Sessions = booking.Preview.Sessions.Select(s => new PTSession
                {
                    PTAvailabilityId = s.TemplateId, PTUserId = request.TrainerId, MemberUserId = actor,
                    SessionDate = s.Date, StartTime = s.StartTime, EndTime = s.EndTime,
                    SessionStatus = PTSessionStatus.CheckoutHold
                }).ToList()
            };
            db.PTPurchases.Add(purchase);
            await db.SaveChangesAsync(ct);
            purchase.HoldExpiresAt = order.CreatedAt.Add(HoldDuration);
            await db.SaveChangesAsync(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, qr, now), 201);
        }, ct);

    public async Task<Result<PTCheckoutResponse>> CheckoutStatus(string actor, string orderNumber, CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTCheckoutResponse>();
        var purchase = await db.PTPurchases.Include(p => p.Order).ThenInclude(o => o!.PaymentTransaction)
            .ThenInclude(p => p!.Invoice)
            .SingleOrDefaultAsync(p => p.MemberUserId == actor && p.Order!.OrderNumber == orderNumber, ct);
        if (purchase?.Order is not { } order)
            return Missing<PTCheckoutResponse>();
        var now = Now;
        var qr = order.OrderStatus == OrderStatus.Pending && purchase.HoldExpiresAt > now
            ? gateway.CreateVietQr(order.OrderNumber, order.PaymentTransaction!.AmountDue) : null;
        return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, qr, now));
    }

    private static PTCheckoutResponse CheckoutResponse(PTPurchase purchase, Order order, VietQrPaymentResponse? qr, DateTime now) =>
        new(purchase.Id, order.OrderNumber,
            (order.OrderStatus == OrderStatus.Pending && purchase.HoldExpiresAt <= now ? OrderStatus.Expired : order.OrderStatus).ToString(),
            order.PaymentTransaction!.AmountDue, DateTime.SpecifyKind(purchase.HoldExpiresAt!.Value, DateTimeKind.Utc),
            qr, order.PaymentTransaction.Invoice?.InvoiceNumber);

    private async Task<VoucherRedemption?> ReserveVoucher(string actor, string code, decimal price, DateTime now, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.Code.ToUpper() == normalized &&
            v.VoucherStatus == VoucherStatus.Active && v.StartAt <= now && v.EndAt > now, ct);
        if (voucher is null || price < voucher.MinimumSpend)
            return null;
        var active = db.VoucherRedemptions.Where(r => r.VoucherId == voucher.Id &&
            (r.RedemptionStatus == VoucherRedemptionStatus.Confirmed ||
             r.RedemptionStatus == VoucherRedemptionStatus.Reserved && r.Order.OrderStatus == OrderStatus.Pending));
        if ((voucher.MaxRedemptions is { } max && await active.CountAsync(ct) >= max) ||
            (voucher.MaxRedemptionsPerUser is { } perUser && await active.CountAsync(r => r.Order.PurchaserUserId == actor, ct) >= perUser))
            return null;
        var discount = voucher.DiscountType == DiscountType.PERCENT ? price * voucher.DiscountValue / 100 : voucher.DiscountValue;
        if (voucher.MaxDiscountAmount is { } cap)
            discount = Math.Min(discount, cap);
        discount = decimal.Floor(discount);
        return discount > 0 && discount < price ? new VoucherRedemption
        {
            VoucherId = voucher.Id, DiscountAmount = discount, RedeemedAt = now
        } : null;
    }

    public async Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct)
    {
        var contract = await db.PTPurchases.Include(p => p.Sessions).SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (contract is null)
            return null;
        var payment = order.PaymentTransaction!;
        var now = clock.GetUtcNow();
        if (payment.GatewayTransactionReference == transfer.TransactionId)
            return Result<SepayWebhookResponse>.Success(new(true));
        var created = new DateTimeOffset(DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc));
        if (contract.Status is not (PTPurchaseStatus.PendingPayment or PTPurchaseStatus.Expired) ||
            order.OrderStatus is not (OrderStatus.Pending or OrderStatus.Expired) ||
            payment.AmountDue != transfer.Amount || transfer.TransferredAt.ToUnixTimeSeconds() < created.ToUnixTimeSeconds() ||
            await payments.SepayReferenceUsedAsync(transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Failure("Không thể xác nhận thanh toán PT.", "PT_PAYMENT_REJECTED", 409);

        var slots = contract.Sessions.Select(s => new PTCalendarSlot(s.PTAvailabilityId ?? 0, s.SessionDate,
            s.StartTime, s.EndTime, "Available")).ToArray();
        var lastDate = slots.Select(s => s.Date).DefaultIfEmpty().Max();
        var redemption = await db.VoucherRedemptions.SingleOrDefaultAsync(r => r.OrderId == order.Id, ct);
        if (contract.Status == PTPurchaseStatus.Expired || order.OrderStatus == OrderStatus.Expired || contract.HoldExpiresAt is not { } expiry ||
            now.UtcDateTime >= expiry || transfer.TransferredAt.UtcDateTime >= expiry ||
            slots.Length != contract.TotalSessions || slots.Length == 0 ||
            !await Coverage(contract.MemberUserId).AnyAsync(m => m.StartDate <= Today && m.EndDate >= lastDate, ct) ||
            await HasConflict(contract.PTUserId, contract.MemberUserId, slots, now.UtcDateTime, ct, ignorePurchase: contract.Id))
        {
            payment.GatewayTransactionReference = transfer.TransactionId;
            payment.AmountReceived = transfer.Amount;
            payment.PaidAt = transfer.TransferredAt.UtcDateTime;
            payment.RawGatewayStatus = "PT_RECONCILIATION_REQUIRED";
            payment.PaymentStatus = PaymentStatus.Cancelled;
            order.OrderStatus = OrderStatus.Expired;
            contract.Status = PTPurchaseStatus.Expired;
            foreach (var session in contract.Sessions)
                session.SessionStatus = PTSessionStatus.Cancelled;
            if (redemption is not null)
                redemption.RedemptionStatus = VoucherRedemptionStatus.Released;
            Audit(null, nameof(PTPurchase), contract.Id, "PT_RECONCILIATION_REQUIRED");
            await db.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        MembershipPurchaseService.RecordPaidOrder(order, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, now);
        contract.Status = PTPurchaseStatus.Active;
        foreach (var session in contract.Sessions)
            session.SessionStatus = PTSessionStatus.Scheduled;
        if (redemption is not null)
            redemption.RedemptionStatus = VoucherRedemptionStatus.Confirmed;
        Notify(contract.MemberUserId, "Đã thanh toán gói PT", $"Hóa đơn {payment.Invoice!.InvoiceNumber}. Lịch tập đã được xác nhận.",
            nameof(PTPurchase), contract.Id);
        Audit(null, nameof(PTPurchase), contract.Id, nameof(PTPurchaseStatus.Active));
        await db.SaveChangesAsync(ct);
        return Result<SepayWebhookResponse>.Success(new(true));
    }
}
