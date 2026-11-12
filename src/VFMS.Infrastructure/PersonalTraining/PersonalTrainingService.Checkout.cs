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
        ApplicationUser TrainerUser, PTAvailability[] Pattern);

    private async Task<Result<Booking>> BuildBooking(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var now = Now;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        if (request.StartDate < today ||
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
        var validity = terms.DurationDays!.Value;
        if (validity <= 0 || validity - 1 > DateOnly.MaxValue.DayNumber - request.StartDate.DayNumber)
            return Invalid<Booking>("Thời hạn gói vượt giới hạn ngày.");
        var end = request.StartDate.AddDays(validity - 1);
        var pattern = await Templates(request.TrainerId).Where(x => request.TemplateIds.Contains(x.Id))
            .OrderBy(x => x.DayOfWeek).ToArrayAsync(ct);
        if (pattern.Length != request.TemplateIds.Length || pattern.Select(x => x.DayOfWeek).Distinct().Count() != pattern.Length)
            return Invalid<Booking>("Chọn một ca đã công bố cho mỗi ngày tập.");
        var dates = ProposedDates(request.StartDate, end, pattern, terms.SessionCount!.Value);
        return Result<Booking>.Success(new(new(package.Id, request.TrainerId, end, package.Price, dates),
            package, profile, trainer, pattern));
    }

    private static PTCalendarSlot[] ProposedDates(DateOnly start, DateOnly end, PTAvailability[] pattern, int count) =>
        PersonalTrainingRules.Occurrences(start, end, pattern, count)
            .Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.EndTime, "Proposed")).ToArray();

    private async Task<Result<MemberMembership>> ValidateHold(string actor, Booking booking, DateTime now, CancellationToken ct)
    {
        var dates = booking.Preview.Sessions;
        if (dates.Count != booking.Package.Components.Single().SessionCount || dates.Any(s =>
                !InHours(booking.TrainerUser.Gym, s.StartTime, s.EndTime) || Utc(s.Date, s.StartTime) <= now.Add(HoldDuration)))
            return Conflict<MemberMembership>("Không đủ lịch trong thời hạn gói PT hoặc có ca ngoài giờ hoạt động.");
        var today = DateOnly.FromDateTime(now.AddHours(7));
        var lastDate = dates[^1].Date;
        var membership = await Coverage(actor).Where(m => m.StartDate <= today && m.EndDate >= lastDate)
            .OrderByDescending(m => m.EndDate).FirstOrDefaultAsync(ct);
        if (membership is null)
            return Result<MemberMembership>.Failure("Cần gói Gym đang hoạt động và bao phủ toàn bộ ngày tập.", "PT_GYM_COVERAGE", 409);
        if (await HasConflict(booking.Preview.TrainerId, actor, dates, now, ct))
            return Conflict<MemberMembership>("Lịch dự kiến trùng lịch của PT, hội viên hoặc Yoga.");
        return Result<MemberMembership>.Success(membership);
    }

    public async Task<Result<PTBookingPreview>> Preview(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var booking = await BuildBooking(actor, request, ct);
        if (!booking.IsSuccess)
            return Result<PTBookingPreview>.Failure(booking.ErrorMessage!, booking.ErrorCode!, booking.StatusCode);
        var proposed = booking.Data!;
        var preview = proposed.Preview;
        var now = Now;
        var suggestions = preview.Sessions.Count != 0 &&
            await HasConflict(request.TrainerId, actor, preview.Sessions, now, ct)
            ? await SuggestSchedules(actor, request, proposed, now, ct) : [];
        return Result<PTBookingPreview>.Success(preview with { Suggestions = suggestions });
    }

    private async Task<PTScheduleSuggestion[]> SuggestSchedules(string actor, PTBookingRequest request,
        Booking booking, DateTime now, CancellationToken ct)
    {
        var level = booking.Trainer.Level;
        var gymId = booking.TrainerUser.GymId;
        var otherTrainers = await ActiveUsers.Include(u => u.Gym)
            .Where(u => u.GymId == gymId && u.UserId != actor && u.UserId != request.TrainerId &&
                u.TrainerProfile != null && u.TrainerProfile.IsActive && u.TrainerProfile.Level == level &&
                u.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.PT)))
            .OrderBy(u => u.UserId).Take(4).ToListAsync(ct);
        var suggestions = new List<PTScheduleSuggestion>();
        var credits = booking.Package.Components.Single().SessionCount!.Value;
        var length = booking.Preview.ExpiresOn.DayNumber - request.StartDate.DayNumber;
        foreach (var trainer in new[] { booking.TrainerUser }.Concat(otherTrainers))
        {
            var templates = await Templates(trainer.UserId).AsNoTracking().ToListAsync(ct);
            var byDay = booking.Pattern.Select(slot => templates.Where(t => t.DayOfWeek == slot.DayOfWeek)
                .OrderBy(t => Math.Abs((t.StartTime - slot.StartTime).TotalMinutes)).ThenBy(t => t.Id).Take(2).ToArray()).ToArray();
            if (byDay.Any(day => day.Length == 0))
                continue;

            var closest = byDay.Select(day => day[0]).ToArray();
            var otherTime = byDay.Select(day => day[^1]).ToArray();
            foreach (var (offset, pattern) in new[] { (0, closest), (0, otherTime), (7, closest), (14, closest) })
            {
                var ids = pattern.Select(t => t.Id).ToArray();
                if (offset > DateOnly.MaxValue.DayNumber - booking.Preview.ExpiresOn.DayNumber ||
                    (trainer.UserId == request.TrainerId && offset == 0 && ids.SequenceEqual(booking.Pattern.Select(t => t.Id))))
                    continue;
                var start = request.StartDate.AddDays(offset);
                if (suggestions.Any(s => s.TrainerId == trainer.UserId && s.StartDate == start && s.TemplateIds.SequenceEqual(ids)))
                    continue;
                var end = start.AddDays(length);
                var dates = ProposedDates(start, end, pattern, credits);
                var candidate = booking with
                {
                    TrainerUser = trainer,
                    Preview = new(booking.Package.Id, trainer.UserId, end, booking.Package.Price, dates)
                };
                if (!(await ValidateHold(actor, candidate, now, ct)).IsSuccess)
                    continue;
                suggestions.Add(new(trainer.UserId, start, ids, dates));
                if (suggestions.Count == 3)
                    return suggestions.ToArray();
                break;
            }
        }
        return suggestions.ToArray();
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
            var eligible = await ValidateHold(actor, booking, now, ct);
            if (!eligible.IsSuccess)
                return Result<PTCheckoutResponse>.Failure(eligible.ErrorMessage!, eligible.ErrorCode!, eligible.StatusCode);
            var order = MembershipPurchaseService.NewOrder(actor, Today, OrderChannel.Online);
            order.Items.Add(MembershipPurchaseService.NewOrderItem(booking.Package, actor, request.StartDate));

            var purchase = new PTPurchase
            {
                Order = order, MemberMembershipId = eligible.Data!.Id, MemberUserId = actor,
                PTUserId = request.TrainerId, Status = PTPurchaseStatus.PendingPayment,
                HoldExpiresAt = now.Add(HoldDuration), PackageNameSnapshot = booking.Package.Name,
                TrainerLevelSnapshot = booking.Trainer.Level, PurchaseDate = Today,
                FreezeMinimumDaysSnapshot = component.PTFreezeMinimumDays ?? 0,
                FreezeMaxTotalDaysSnapshot = component.PTFreezeMaxTotalDays ?? 0,
                StartDate = request.StartDate, EndDate = booking.Preview.ExpiresOn,
                PurchasePrice = booking.Package.Price, TotalSessions = booking.Preview.Sessions.Count,
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
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, null, now), 201);
        }, ct);

    public Task<Result<PTCheckoutResponse>> StartPayment(string actor, string orderNumber, PTPaymentRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Member, ct) is null)
                return Forbidden<PTCheckoutResponse>();
            var order = await payments.FindOrderAsync(orderNumber, actor, forUpdate: true, ct);
            if (order is null)
                return Missing<PTCheckoutResponse>();
            var purchase = await db.PTPurchases.SingleOrDefaultAsync(p => p.OrderId == order.Id && p.MemberUserId == actor, ct);
            if (purchase is null)
                return Missing<PTCheckoutResponse>();
            var now = Now;
            if (purchase.Status != PTPurchaseStatus.PendingPayment || purchase.HoldExpiresAt is not { } expiry ||
                expiry <= now || order.OrderStatus != OrderStatus.Pending)
                return Conflict<PTCheckoutResponse>("Đơn PT đã hết hạn hoặc đã được xử lý.");

            if (order.PaymentTransaction is { } issued)
            {
                var existingQr = gateway.CreateVietQr(orderNumber, issued.AmountDue);
                return existingQr is null
                    ? Result<PTCheckoutResponse>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503)
                    : Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, existingQr, now));
            }

            VoucherRedemption? voucher = null;
            if (!string.IsNullOrWhiteSpace(request.VoucherCode))
            {
                voucher = await ReserveVoucher(actor, request.VoucherCode, purchase.PurchasePrice, now, ct);
                if (voucher is null)
                    return Result<PTCheckoutResponse>.Failure("Voucher không hợp lệ hoặc đã hết lượt.", "PT_VOUCHER_INVALID");
            }
            var due = purchase.PurchasePrice - (voucher?.DiscountAmount ?? 0);
            var qr = gateway.CreateVietQr(order.OrderNumber, due);
            if (qr is null)
                return Result<PTCheckoutResponse>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);
            order.VoucherRedemption = voucher;
            order.PaymentTransaction = new PaymentTransaction
            {
                UserId = actor, GatewayName = "SePay", PaymentMethod = PaymentMethod.VietQR, AmountDue = due
            };
            purchase.PurchasePrice = due;
            await db.SaveChangesAsync(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, qr, now));
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
        var qr = order.OrderStatus == OrderStatus.Pending && purchase.HoldExpiresAt > now &&
            order.PaymentTransaction is { } payment ? gateway.CreateVietQr(order.OrderNumber, payment.AmountDue) : null;
        return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, qr, now));
    }

    private static PTCheckoutResponse CheckoutResponse(PTPurchase purchase, Order order, VietQrPaymentResponse? qr, DateTime now) =>
        new(purchase.Id, order.OrderNumber,
            (order.OrderStatus == OrderStatus.Pending && purchase.HoldExpiresAt <= now ? OrderStatus.Expired : order.OrderStatus).ToString(),
            order.PaymentTransaction?.AmountDue ?? purchase.PurchasePrice,
            DateTime.SpecifyKind(purchase.HoldExpiresAt!.Value, DateTimeKind.Utc),
            qr, order.PaymentTransaction?.Invoice?.InvoiceNumber);

    private async Task<VoucherRedemption?> ReserveVoucher(string actor, string code, decimal price, DateTime now, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.Code.ToUpper() == normalized &&
            v.VoucherStatus == VoucherStatus.Active && v.StartAt <= now && v.EndAt > now, ct);
        if (voucher is null || price < voucher.MinimumSpend)
            return null;
        if (!await VoucherHasCapacity(voucher, actor, ct))
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

    private async Task<bool> VoucherHasCapacity(Voucher voucher, string actor, CancellationToken ct, int? excludingOrder = null)
    {
        var active = db.VoucherRedemptions.Where(r => r.VoucherId == voucher.Id &&
            (excludingOrder == null || r.OrderId != excludingOrder) &&
            (r.RedemptionStatus == VoucherRedemptionStatus.Confirmed ||
             r.RedemptionStatus == VoucherRedemptionStatus.Reserved && r.Order.OrderStatus == OrderStatus.Pending));
        if (voucher.MaxRedemptions is { } max && await active.CountAsync(ct) >= max)
            return false;
        return voucher.MaxRedemptionsPerUser is not { } perUser ||
            await active.CountAsync(r => r.Order.PurchaserUserId == actor, ct) < perUser;
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
        var redemption = await db.VoucherRedemptions.Include(r => r.Voucher)
            .SingleOrDefaultAsync(r => r.OrderId == order.Id, ct);
        if (contract.HoldExpiresAt is not { } expiry || transfer.TransferredAt.UtcDateTime >= expiry ||
            slots.Length != contract.TotalSessions || slots.Length == 0 ||
            slots.Any(s => Utc(s.Date, s.StartTime) <= now.UtcDateTime) ||
            (redemption?.RedemptionStatus == VoucherRedemptionStatus.Released &&
                !await VoucherHasCapacity(redemption.Voucher, contract.MemberUserId, ct, order.Id)) ||
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
