using System.Text.Json;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    private static readonly TimeSpan PaymentDuration = TimeSpan.FromMinutes(PackageOrderTiming.OnlinePaymentMinutes);
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(PackageOrderTiming.PtOnlineHoldMinutes);
    // GBR-96: the Member must click 'Proceed to Payment' within the first 10 minutes of the order.
    private static readonly TimeSpan ProceedWindow = TimeSpan.FromMinutes(PackageOrderTiming.PtChoosePaymentMinutes);
    // Counter (cash) orders hold the slots as long as a membership cash order (NewScope: UC 4.4).
    private static readonly TimeSpan CounterHold = TimeSpan.FromHours(PackageOrderTiming.CounterPaymentHours);
    private static DateTime PaymentDeadline(Order order) =>
        order.CreatedAt.Add(order.Channel == OrderChannel.Counter ? CounterHold : PaymentDuration);
    private async Task<ApplicationUser?> Purchaser(string actor, CancellationToken ct) =>
        await User(actor, UserRole.Member, ct) ?? await User(actor, UserRole.Customer, ct);
    private sealed record Booking(PTBookingPreview Preview, Package Package, TrainerProfile Trainer,
        ApplicationUser TrainerUser, PTAvailability[] Pattern);

    private async Task<Result<Booking>> BuildBooking(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var now = Now;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        if (!PersonalTrainingRules.CanStartOn(request.StartDate, today))
            return Invalid<Booking>("Chọn ngày bắt đầu trong tương lai và đăng ký trước buổi tập đầu tiên ít nhất 24 giờ.");
        if (request.TemplateIds.Length == 0 ||
            request.TemplateIds.Distinct().Count() != request.TemplateIds.Length)
            return Invalid<Booking>("Ngày bắt đầu hoặc mẫu lịch không hợp lệ.");
        var member = await Purchaser(actor, ct);
        if (member is null)
            return Forbidden<Booking>();
        var trainer = await User(request.TrainerId, UserRole.PT, ct);
        if (trainer is null || trainer.GymId != member.GymId || actor == request.TrainerId)
            return Missing<Booking>();
        if (await TeachesYoga(request.TrainerId, ct))
            return Conflict<Booking>("Huấn luyện viên đang phụ trách lớp Yoga.");
        var profile = await repo.GetPTProfileAsync(request.TrainerId, ct);
        var package = await repo.GetPTPackageAsync(request.PackageId, activeOnly: true, ct);
        if (profile is null || package is null || package.Components.Single().ApplicableTrainerLevel != profile.Level)
            return Invalid<Booking>("Gói PT hoặc cấp độ huấn luyện viên không hợp lệ.");
        if (!profile.AcceptingNewMembers)
            return Conflict<Booking>("Huấn luyện viên hiện không nhận học viên mới.");
        var terms = package.Components.Single();
        var pattern = (await Templates(request.TrainerId, ct)).Where(x => request.TemplateIds.Contains(x.Id))
            .OrderBy(x => x.DayOfWeek).ToArray();
        if (pattern.Length != request.TemplateIds.Length || pattern.Select(x => x.DayOfWeek).Distinct().Count() != pattern.Length)
            return Invalid<Booking>("Chọn một ca đã công bố cho mỗi ngày tập.");
        var count = terms.SessionCount.GetValueOrDefault();
        var candidates = Candidates(request.StartDate, pattern, count);
        var (dates, skipped) = Schedule(candidates, await ConflictingSlots(request.TrainerId, actor, candidates, now, ct), count);
        // Busy in the start week (or too often to move): keep the plain schedule so the hold reports the conflict (MSG-30).
        if (dates.Length == 0)
            dates = candidates.Take(count).ToArray();
        if (dates.Length < count || count <= 0)
            return Invalid<Booking>("Không thể xếp đủ số buổi từ ngày bắt đầu đã chọn.");
        if (!PersonalTrainingRules.HasBookingNotice(dates[0].Date, dates[0].StartTime, now))
            return Invalid<Booking>("Cần đăng ký trước buổi tập đầu tiên ít nhất 24 giờ.");
        return Result<Booking>.Success(new(new(package.Id, request.TrainerId, dates[^1].Date, package.Price, dates)
        { Skipped = skipped }, package, profile, trainer, pattern));
    }

    // UC 4.2 / GBR-12: a slot busy in the start week cannot be used (another schedule holds it). A later busy date is
    // skipped and the session moves to the next free occurrence of the pattern after the last session.
    // shortcut: looks ahead at most twice the session count; a pattern busy more often than that cannot be booked.
    private static PTCalendarSlot[] Candidates(DateOnly start, PTAvailability[] pattern, int count) =>
        count <= 0 ? [] : ProposedDates(start, pattern, count * 2);

    private static (PTCalendarSlot[] Dates, PTCalendarSlot[] Skipped) Schedule(PTCalendarSlot[] candidates,
        IReadOnlySet<PTCalendarSlot> busy, int count)
    {
        var dates = candidates.Where(slot => !busy.Contains(slot)).Take(count).ToArray();
        var startWeekEnds = candidates.Length == 0 ? DateOnly.MinValue : candidates[0].Date.AddDays(7);
        if (count <= 0 || dates.Length < count || candidates.Any(slot => slot.Date < startWeekEnds && busy.Contains(slot)))
            return ([], []);
        return (dates, candidates.Where(slot => busy.Contains(slot) && slot.Date <= dates[^1].Date).ToArray());
    }

    private static PTCalendarSlot[] ProposedDates(DateOnly start, PTAvailability[] pattern, int count)
    {
        if (count <= 0 || pattern.Length == 0)
            return [];
        var wholeWeeks = (count - 1) / pattern.Length;
        if (wholeWeeks * 7L > DateOnly.MaxValue.DayNumber - start.DayNumber)
            return [];
        var lastPossibleDay = Math.Min(DateOnly.MaxValue.DayNumber, start.DayNumber + wholeWeeks * 7L + 6);
        var dates = PersonalTrainingRules.Occurrences(start, DateOnly.FromDayNumber((int)lastPossibleDay), pattern, count);
        return dates.Count == count
            ? dates.Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.EndTime, "Proposed")).ToArray()
            : [];
    }

    private async Task<Result<bool>> ValidateHold(string actor, Booking booking, DateTime now, CancellationToken ct)
    {
        var dates = booking.Preview.Sessions;
        if (dates.Count != booking.Package.Components.Single().SessionCount || dates.Any(s =>
                !InHours(booking.TrainerUser.Gym, s.StartTime, s.EndTime) ||
                !PersonalTrainingRules.HasBookingNotice(s.Date, s.StartTime, now)))
            return Conflict<bool>("Không đủ số buổi hợp lệ, có ca ngoài giờ gym hoặc chưa đủ 24 giờ trước buổi tập đầu tiên.");
        if (await HasConflict(booking.Preview.TrainerId, actor, dates, now, ct))
            return await HasMemberBookingConflict(actor, dates, now, ct)
                ? Result<bool>.Failure("Lịch dự kiến trùng lịch PT hoặc Yoga của bạn.", "PT_MEMBER_SCHEDULE_CONFLICT", 409)
                : Result<bool>.Failure("Ca của PT đã được giữ, đã đặt hoặc trùng lịch Yoga.", "PT_TRAINER_SLOT_UNAVAILABLE", 409);
        return Result<bool>.Success(true);
    }

    private async Task<bool> HasMemberBookingConflict(string actor, IReadOnlyList<PTCalendarSlot> slots, DateTime now,
        CancellationToken ct, int? ignoreSession = null)
    {
        var dates = slots.Select(s => s.Date).Distinct().ToArray();
        var pt = (await repo.GetOccupyingSessionsAsync(now, dates.Min(), dates.Max(), null, actor, ignoreSession, cancellationToken: ct))
            .Where(s => dates.Contains(s.SessionDate)).ToList();
        if (slots.Any(slot => pt.Any(s => s.SessionDate == slot.Date &&
                PersonalTrainingRules.Overlaps(slot.StartTime, slot.EndTime, s.StartTime, s.EndTime))))
            return true;
        var yoga = await repo.GetYogaSessionsAsync(null, actor, dates.Min(), dates.Max(), ct);
        return slots.Any(slot => yoga.Any(s => s.ScheduledDate == slot.Date &&
            PersonalTrainingRules.Overlaps(slot.StartTime, slot.EndTime, s.StartTime, s.EndTime)));
    }

    public async Task<Result<PTBookingPreview>> Preview(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var booking = await BuildBooking(actor, request, ct);
        if (!booking.IsSuccess)
            return Result<PTBookingPreview>.Failure(booking.ErrorMessage!, booking.ErrorCode!, booking.StatusCode);
        return Result<PTBookingPreview>.Success(booking.Data!.Preview);
    }

    public async Task<Result<PTWeeklySlot[]>> BookingSlots(string actor, PTBookingSlotsRequest request, CancellationToken ct)
    {
        if (!PersonalTrainingRules.CanStartOn(request.StartDate, Today))
            return Invalid<PTWeeklySlot[]>("Chọn ngày bắt đầu trong tương lai và đăng ký trước buổi tập đầu tiên ít nhất 24 giờ.");
        var member = await Purchaser(actor, ct);
        if (member is null)
            return Forbidden<PTWeeklySlot[]>();
        var trainer = await User(request.TrainerId, UserRole.PT, ct);
        if (trainer is null || trainer.GymId != member.GymId || actor == request.TrainerId)
            return Missing<PTWeeklySlot[]>();

        var level = (await repo.GetPTProfileAsync(request.TrainerId, ct))?.Level;
        var package = await repo.GetPTPackageAsync(request.PackageId, activeOnly: true, ct);
        var component = package?.Components.Single();
        if (level is null || component is null || component.ApplicableTrainerLevel != level)
            return Invalid<PTWeeklySlot[]>("Gói PT hoặc cấp độ huấn luyện viên không hợp lệ.");

        var templates = (await Templates(request.TrainerId, ct)).OrderBy(s => s.StartTime).ToArray();
        if (templates.Length == 0)
            return Result<PTWeeklySlot[]>.Success([]);
        var selected = templates.Where(s => request.TemplateIds.Contains(s.Id))
            .DistinctBy(s => s.DayOfWeek).ToArray();
        var count = component.SessionCount.GetValueOrDefault();
        var candidates = templates.ToDictionary(s => s.Id, s => Candidates(request.StartDate,
            selected.Where(item => item.DayOfWeek != s.DayOfWeek).Append(s).ToArray(), count));
        var now = Now;
        var conflicts = await ConflictingSlots(request.TrainerId, actor,
            candidates.Values.SelectMany(slots => slots).Distinct().ToArray(), now, ct);
        // GBR-12: busy dates move to the end, so a slot is blocked only when the schedule cannot be completed.
        var patterns = candidates.ToDictionary(item => item.Key, item => Schedule(item.Value, conflicts, count).Dates);
        var dates = candidates.Values.SelectMany(slots => slots).ToArray();
        var through = dates.Length == 0 ? request.StartDate : dates.Max(s => s.Date);
        var trainerBusy = await repo.GetOccupyingSessionsAsync(now, request.StartDate, through, request.TrainerId, null,
            cancellationToken: ct);
        var yogaAssigned = await TeachesYoga(request.TrainerId, ct);

        return Result<PTWeeklySlot[]>.Success(templates.Select(slot =>
        {
            var pattern = patterns[slot.Id];
            var proposed = pattern.Where(s => s.TemplateId == slot.Id).ToArray();
            // A slot that can still complete the schedule is available; its busy dates are moved to the end.
            if (pattern.Length > 0 && !yogaAssigned &&
                PersonalTrainingRules.HasBookingNotice(pattern[0].Date, pattern[0].StartTime, now) && proposed.All(s =>
                    InHours(trainer.Gym, s.StartTime, s.EndTime)))
                return new PTWeeklySlot(slot.Id, slot.DayOfWeek!.Value, slot.StartTime, slot.EndTime, null);
            proposed = candidates[slot.Id].Take(count).Where(s => s.TemplateId == slot.Id).ToArray();
            var reason = yogaAssigned || proposed.Length == 0 || proposed.Any(s =>
                !InHours(trainer.Gym, s.StartTime, s.EndTime) || !PersonalTrainingRules.HasBookingNotice(s.Date, s.StartTime, now))
                ? "Không khả dụng"
                : proposed.Any(d => trainerBusy.Any(s => s.SessionStatus != PTSessionStatus.CheckoutHold &&
                    s.SessionDate == d.Date && PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime)))
                    ? "Đã có người đặt"
                    : proposed.Any(d => trainerBusy.Any(s => s.SessionStatus == PTSessionStatus.CheckoutHold &&
                        s.SessionDate == d.Date && PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime)))
                        ? "Đang giữ chỗ"
                        : proposed.Any(conflicts.Contains) ? "Trùng lịch PT hoặc Yoga" : null;
            return new PTWeeklySlot(slot.Id, slot.DayOfWeek!.Value, slot.StartTime, slot.EndTime, reason);
        }).ToArray());
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
                Order = order,
                MemberUserId = actor,
                PTUserId = request.TrainerId,
                Status = PTPurchaseStatus.PendingPayment,
                HoldExpiresAt = now.Add(HoldDuration),
                PackageNameSnapshot = booking.Package.Name,
                TrainerLevelSnapshot = booking.Trainer.Level,
                PurchaseDate = Today,
                FreezeMinimumDaysSnapshot = component.PTFreezeMinimumDays ?? 0,
                FreezeMaxTotalDaysSnapshot = component.PTFreezeMaxTotalDays ?? 0,
                StartDate = request.StartDate,
                EndDate = booking.Preview.ExpiresOn,
                PurchasePrice = booking.Package.Price,
                TotalSessions = booking.Preview.Sessions.Count,
                RemainingSessions = booking.Preview.Sessions.Count,
                WeeklyPatternJson = JsonSerializer.Serialize(new SessionBasedPattern(
                    booking.Pattern.Select(p => new PTSlotInput(p.DayOfWeek!.Value, p.StartTime)).ToArray())),
                Sessions = booking.Preview.Sessions.Select(s => new PTSession
                {
                    PTAvailabilityId = s.TemplateId,
                    PTUserId = request.TrainerId,
                    MemberUserId = actor,
                    SessionDate = s.Date,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    SessionStatus = PTSessionStatus.CheckoutHold
                }).ToList()
            };
            repo.Add(purchase);
            await repo.SaveChangesAsync(ct);
            purchase.HoldExpiresAt = order.CreatedAt.Add(HoldDuration);
            await repo.SaveChangesAsync(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, null, now), 201);
        }, ct);

    public Task<Result<PTCheckoutResponse>> StartPayment(string actor, string orderNumber, PTPaymentRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            var member = await Purchaser(actor, ct);
            if (member is null)
                return Forbidden<PTCheckoutResponse>();
            var code = string.IsNullOrWhiteSpace(request.VoucherCode) ? null : VoucherRules.NormalizeCode(request.VoucherCode);
            var campaign = code is null ? null : await payments.FindVoucherForUpdateAsync(code, ct);
            var order = await payments.FindOrderAsync(orderNumber, actor, forUpdate: true, ct);
            if (order is null)
                return Missing<PTCheckoutResponse>();
            var purchase = await repo.GetContractForOrderAsync(order.Id, ct);
            if (purchase is null || purchase.MemberUserId != actor)
                return Missing<PTCheckoutResponse>();
            var now = Now;
            if (order.OrderStatus == OrderStatus.Paid && order.PaymentTransaction?.PaymentMethod == PaymentMethod.NoCharge)
                return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, null, now));
            if (purchase.Status != PTPurchaseStatus.PendingPayment || PaymentDeadline(order) <= now ||
                order.OrderStatus != OrderStatus.Pending)
                return Conflict<PTCheckoutResponse>("Đơn PT đã hết hạn hoặc đã được xử lý.");

            if (code is not null && (order.Channel == OrderChannel.Counter || order.PaymentTransaction is not null))
            {
                var applied = order.VoucherRedemption;
                if (applied is null || !(string.Equals(applied.VoucherCodeSnapshot, code, StringComparison.OrdinalIgnoreCase) ||
                    campaign?.Id == applied.VoucherId))
                    return Conflict<PTCheckoutResponse>("Đơn PT đã chốt số tiền thanh toán và không thể đổi voucher.");
            }
            if (order.Channel == OrderChannel.Counter)
                return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, null, now));
            if (order.PaymentTransaction is null && order.CreatedAt.Add(ProceedWindow) <= now)
                return Result<PTCheckoutResponse>.Failure(
                    "Đã quá 10 phút để bắt đầu thanh toán. Vui lòng đặt lại lịch.", "PT_PAYMENT_WINDOW_CLOSED", 409);

            if (order.PaymentTransaction is { } issued)
            {
                var existingQr = gateway.CreateVietQr(orderNumber, issued.AmountDue);
                return existingQr is null
                    ? Result<PTCheckoutResponse>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503)
                    : Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, existingQr, now));
            }

            VoucherRedemption? voucher = null;
            if (code is not null)
            {
                if (campaign is null)
                    return Result<PTCheckoutResponse>.Failure("Không tìm thấy mã voucher.", "VOUCHER_NOT_FOUND");
                var capacity = await payments.ReadVoucherCapacityAsync(campaign.Id, member, ct);
                var check = VoucherCheckoutRules.Check(campaign, member.CreatedAt, now, capacity, purchase.PurchasePrice);
                if (!check.IsSuccess)
                    return Result<PTCheckoutResponse>.Failure(check.ErrorMessage!, check.ErrorCode!, check.StatusCode);
                voucher = new VoucherRedemption
                {
                    VoucherId = campaign.Id,
                    VoucherGrantId = campaign.Audience == VoucherAudience.AssignedUsers ? capacity.AvailableGrantId : null,
                    VoucherCodeSnapshot = campaign.Code,
                    VoucherNameSnapshot = campaign.Name,
                    DiscountAmount = check.Data,
                    RedeemedAt = now
                };
            }
            var due = purchase.PurchasePrice - (voucher?.DiscountAmount ?? 0);
            if (due == 0 && voucher is not null)
            {
                if (!await HeldSlotsStillValid(purchase, now, ct))
                    return Conflict<PTCheckoutResponse>("Lịch PT không còn hợp lệ.");
                order.VoucherRedemption = voucher;
                order.PaymentTransaction = new PaymentTransaction
                {
                    UserId = actor, GatewayName = "NoCharge", PaymentMethod = PaymentMethod.NoCharge, AmountDue = 0
                };
                purchase.PurchasePrice = 0;
                MembershipPurchaseService.RecordPaidOrder(order, 0, now, null, now);
                ActivatePTContract(purchase);
                Notify(actor, "Đã thanh toán gói PT", $"Hóa đơn {order.PaymentTransaction.Invoice!.InvoiceNumber}. Lịch tập đã được xác nhận.",
                    nameof(PTPurchase), purchase.Id);
                await repo.SaveChangesAsync(ct);
                return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, null, now));
            }
            if (request.PaymentMethod == MembershipPaymentMethods.Cash)
            {
                // The payment row is created when Staff confirms the cash, so SePay and membership cash confirmation never match it.
                order.Channel = OrderChannel.Counter;
                order.VoucherRedemption = voucher;
                purchase.PurchasePrice = due;
                purchase.HoldExpiresAt = order.CreatedAt.Add(CounterHold);
                await repo.SaveChangesAsync(ct);
                return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, null, now));
            }
            var qr = gateway.CreateVietQr(order.OrderNumber, due);
            if (qr is null)
                return Result<PTCheckoutResponse>.Failure("VietQR chưa được cấu hình.", "PAYMENT_UNAVAILABLE", 503);
            order.VoucherRedemption = voucher;
            order.PaymentTransaction = new PaymentTransaction
            {
                UserId = actor,
                GatewayName = "SePay",
                PaymentMethod = PaymentMethod.VietQR,
                AmountDue = due
            };
            purchase.PurchasePrice = due;
            await repo.SaveChangesAsync(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, qr, now));
        }, ct);

    public Task<Result<PTCheckoutResponse>> ConfirmCash(string staffId, string orderNumber, decimal amount, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await User(staffId, UserRole.Staff, ct);
            if (staff is null)
                return Result<PTCheckoutResponse>.Failure("Cần quyền nhân viên lễ tân.", "CASH_CONFIRMATION_FORBIDDEN", 403);
            var order = await payments.FindOrderAsync(orderNumber, purchaserId: null, forUpdate: true, ct);
            var contract = order is null || order.Purchaser.GymId != staff.GymId ? null
                : await repo.GetContractForOrderAsync(order.Id, ct);
            if (order is null || contract is null)
                return Missing<PTCheckoutResponse>();

            var now = clock.GetUtcNow();
            if (order.OrderStatus == OrderStatus.Paid && order.PaymentTransaction is { PaymentMethod: PaymentMethod.Cash } paid &&
                paid.AmountReceived == amount)
                return Result<PTCheckoutResponse>.Success(CheckoutResponse(contract, order, null, now.UtcDateTime));
            if (order.Channel != OrderChannel.Counter || order.PaymentTransaction is not null || order.OrderStatus != OrderStatus.Pending ||
                contract.Status != PTPurchaseStatus.PendingPayment || contract.HoldExpiresAt <= now.UtcDateTime)
                return Result<PTCheckoutResponse>.Failure(
                    "Đơn không chờ thanh toán tại quầy hoặc đã hết hạn giữ lịch.", "PT_CASH_NOT_PENDING", 409);
            if (amount != contract.PurchasePrice)
                return Result<PTCheckoutResponse>.Failure(
                    $"Số tiền nhận phải đúng {contract.PurchasePrice:N0} VND.", "PT_CASH_AMOUNT_MISMATCH", 409);
            if (!await HeldSlotsStillValid(contract, now.UtcDateTime, ct))
                return Conflict<PTCheckoutResponse>("Lịch PT không còn hợp lệ.");

            order.PaymentTransaction = new PaymentTransaction
            {
                UserId = contract.MemberUserId,
                GatewayName = "Cash",
                PaymentMethod = PaymentMethod.Cash,
                AmountDue = contract.PurchasePrice
            };
            MembershipPurchaseService.RecordPaidOrder(order, amount, now, null, now);
            ActivatePTContract(contract);
            Audit(staffId, nameof(PTPurchase), contract.Id, "CashPaymentConfirmed");
            Notify(contract.MemberUserId, "Đã thanh toán gói PT", $"Hóa đơn {order.PaymentTransaction.Invoice!.InvoiceNumber}. Lịch tập đã được xác nhận.",
                nameof(PTPurchase), contract.Id);
            await repo.SaveChangesAsync(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(contract, order, null, now.UtcDateTime));
        }, ct);

    private async Task<bool> HeldSlotsStillValid(PTPurchase contract, DateTime now, CancellationToken ct)
    {
        var slots = contract.Sessions.Where(s => s.SessionStatus == PTSessionStatus.CheckoutHold)
            .Select(s => new PTCalendarSlot(s.PTAvailabilityId ?? 0, s.SessionDate, s.StartTime, s.EndTime, "Proposed"))
            .ToArray();
        var trainer = await User(contract.PTUserId, UserRole.PT, ct);
        return slots.Length > 0 && slots.Length == contract.TotalSessions && trainer is not null &&
            !slots.Any(s => Utc(s.Date, s.StartTime) <= now || !InHours(trainer.Gym, s.StartTime, s.EndTime)) &&
            !await TeachesYoga(contract.PTUserId, ct) &&
            !await HasConflict(contract.PTUserId, contract.MemberUserId, slots, now, ct, ignorePurchase: contract.Id);
    }

    public async Task<Result<PTCheckoutResponse>> CheckoutStatus(string actor, string orderNumber, CancellationToken ct)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<PTCheckoutResponse>();
        var purchase = await repo.GetMemberContractByOrderNumberAsync(actor, orderNumber, ct);
        if (purchase?.Order is not { } order)
            return Missing<PTCheckoutResponse>();
        var now = Now;
        var qr = order.OrderStatus == OrderStatus.Pending && PaymentDeadline(order) > now &&
            order.PaymentTransaction is { } payment ? gateway.CreateVietQr(order.OrderNumber, payment.AmountDue) : null;
        var response = CheckoutResponse(purchase, order, qr, now);
        if (order.PaymentTransaction is not { } original)
            return Result<PTCheckoutResponse>.Success(response);
        var refunds = await repo.GetRefundsAsync(original.Id, ct);
        return Result<PTCheckoutResponse>.Success(response with
        {
            Refunds = refunds.Select(payment => new CheckoutRefundView(payment.Id, order.OrderNumber,
                payment.RefundAmount!.Value, payment.RefundStatus!, DateTime.SpecifyKind(payment.PaidAt!.Value, DateTimeKind.Utc),
                payment.RefundResolvedAt is { } resolved ? DateTime.SpecifyKind(resolved, DateTimeKind.Utc) : null,
                payment.GatewayTransactionReference!, payment.RefundReference)).ToArray()
        });
    }

    private static PTCheckoutResponse CheckoutResponse(PTPurchase purchase, Order order, VietQrPaymentResponse? qr, DateTime now) =>
        new(purchase.Id, order.OrderNumber,
            order.PaymentTransaction?.RefundStatus switch
            {
                "Pending" => "RefundPending",
                "Refunded" => "Refunded",
                "Failed" => "RefundFailed",
                _ when order.OrderStatus == OrderStatus.Pending && purchase.HoldExpiresAt <= now => "Expired",
                _ when order.OrderStatus == OrderStatus.Pending && PaymentDeadline(order) <= now => "AwaitingConfirmation",
                _ => order.OrderStatus.ToString()
            },
            order.PaymentTransaction?.AmountDue ?? purchase.PurchasePrice,
            DateTime.SpecifyKind(PaymentDeadline(order), DateTimeKind.Utc),
            qr, order.PaymentTransaction?.Invoice?.InvoiceNumber, DateTime.SpecifyKind(purchase.HoldExpiresAt!.Value, DateTimeKind.Utc),
            ProceedBefore: DateTime.SpecifyKind(order.CreatedAt.Add(ProceedWindow), DateTimeKind.Utc),
            PayAtCounter: order.Channel == OrderChannel.Counter);

    private async Task<bool> RestoreReleasedVoucher(Order order, VoucherRedemption redemption, DateTime now, CancellationToken ct)
    {
        // The callback holds a serializable transaction. A released hold must qualify again
        // before accepting a timely transfer that reached the system after JOB-01.
        var voucher = redemption.Voucher;
        if (voucher is null) return false;
        var capacity = await payments.ReadVoucherCapacityAsync(voucher.Id, order.Purchaser, ct);
        var check = VoucherCheckoutRules.Check(voucher, order.Purchaser.CreatedAt, now, capacity,
            order.Items.Sum(i => i.UnitPriceSnapshot));
        if (!check.IsSuccess || check.Data != redemption.DiscountAmount) return false;
        redemption.VoucherGrantId = voucher.Audience == VoucherAudience.AssignedUsers ? capacity.AvailableGrantId : null;
        redemption.RedemptionStatus = VoucherRedemptionStatus.Reserved;
        return true;
    }

    public async Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct)
    {
        var contract = await repo.GetContractForOrderAsync(order.Id, ct);
        if (contract is null)
            return null;
        var payment = order.PaymentTransaction!;
        var now = clock.GetUtcNow();
        if (await repo.IsTransferRecordedAsync(payment, transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Success(new(true));
        var created = new DateTimeOffset(DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc));
        if (transfer.TransferredAt.ToUnixTimeSeconds() < created.ToUnixTimeSeconds() ||
            await payments.SepayReferenceUsedAsync(transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Failure("Không thể xác nhận thanh toán PT.", "PT_PAYMENT_REJECTED", 409);
        if (payment.GatewayTransactionReference is not null || order.OrderStatus == OrderStatus.Paid)
        {
            repo.RecordExtraTransfer(payment, transfer, now.UtcDateTime);
            await repo.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        var slots = contract.Sessions.Select(s => new PTCalendarSlot(s.PTAvailabilityId ?? 0, s.SessionDate,
            s.StartTime, s.EndTime, "Available")).ToArray();
        var trainer = await User(contract.PTUserId, UserRole.PT, ct);
        var redemption = await repo.GetRedemptionForOrderAsync(order.Id, ct);
        if (contract.Status is not (PTPurchaseStatus.PendingPayment or PTPurchaseStatus.Expired) ||
            order.OrderStatus is not (OrderStatus.Pending or OrderStatus.Expired) ||
            payment.AmountDue != transfer.Amount || transfer.TransferredAt.UtcDateTime >= PaymentDeadline(order) ||
            slots.Length != contract.TotalSessions || slots.Length == 0 ||
            trainer is null || slots.Any(s => Utc(s.Date, s.StartTime) <= now.UtcDateTime ||
                !InHours(trainer.Gym, s.StartTime, s.EndTime)) ||
            (contract.HoldExpiresAt <= now.UtcDateTime && !await OriginalTemplatesAvailable(contract.PTUserId, slots, ct)) ||
            await TeachesYoga(contract.PTUserId, ct) ||
            await HasConflict(contract.PTUserId, contract.MemberUserId, slots, now.UtcDateTime, ct, ignorePurchase: contract.Id) ||
            (redemption?.RedemptionStatus == VoucherRedemptionStatus.Released &&
                !await RestoreReleasedVoucher(order, redemption, now.UtcDateTime, ct)))
        {
            repo.RecordUnappliedTransfer(payment, transfer, now.UtcDateTime);
            contract.Status = PTPurchaseStatus.Expired;
            foreach (var session in contract.Sessions)
                session.SessionStatus = PTSessionStatus.Cancelled;
            if (redemption is not null)
                redemption.RedemptionStatus = VoucherRedemptionStatus.Released;
            Audit(null, nameof(PTPurchase), contract.Id, "PT_REFUND_REQUESTED");
            Notify(contract.MemberUserId, "Thanh toán PT chưa được áp dụng",
                $"Đã nhận {transfer.Amount:N0} VND cho đơn {order.OrderNumber} nhưng không thể xác nhận lịch tập. " +
                "Vui lòng liên hệ quầy lễ tân để được hoàn tiền.", nameof(PTPurchase), contract.Id);
            await repo.SaveChangesAsync(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        MembershipPurchaseService.RecordPaidOrder(order, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, now);
        ActivatePTContract(contract);
        Notify(contract.MemberUserId, "Đã thanh toán gói PT", $"Hóa đơn {payment.Invoice!.InvoiceNumber}. Lịch tập đã được xác nhận.",
            nameof(PTPurchase), contract.Id);
        Audit(null, nameof(PTPurchase), contract.Id, nameof(PTPurchaseStatus.Active));
        await repo.SaveChangesAsync(ct);
        return Result<SepayWebhookResponse>.Success(new(true));
    }

    private static void ActivatePTContract(PTPurchase contract)
    {
        contract.Status = PTPurchaseStatus.Active;
        foreach (var session in contract.Sessions)
            session.SessionStatus = PTSessionStatus.Scheduled;
    }

    private async Task<bool> OriginalTemplatesAvailable(string trainerId, PTCalendarSlot[] slots, CancellationToken ct)
    {
        var template = await Templates(trainerId, ct);
        return slots.All(slot => template.Any(s => s.DayOfWeek == slot.Date.DayOfWeek &&
            s.StartTime == slot.StartTime && s.EndTime == slot.EndTime));
    }
}
