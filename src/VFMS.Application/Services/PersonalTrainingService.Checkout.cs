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
    private static readonly TimeSpan PaymentDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(20);
    // GBR-96: the Member must click 'Proceed to Payment' within the first 10 minutes of the order.
    private static readonly TimeSpan ProceedWindow = TimeSpan.FromMinutes(10);
    // Counter (cash) orders hold the slots as long as a membership cash order (NewScope: UC 4.4).
    private static readonly TimeSpan CounterHold = TimeSpan.FromHours(24);
    private const string DemoGateway = "PTDemo";
    private static DateTime PaymentDeadline(Order order) =>
        order.CreatedAt.Add(order.Channel == OrderChannel.Counter ? CounterHold : PaymentDuration);
    private sealed record Booking(PTBookingPreview Preview, Package Package, TrainerProfile Trainer,
        ApplicationUser TrainerUser, PTAvailability[] Pattern);

    private async Task<Result<Booking>> BuildBooking(string actor, PTBookingRequest request, CancellationToken ct)
    {
        var now = Now;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        if (!PersonalTrainingRules.CanStartOn(request.StartDate, today))
            return Invalid<Booking>("Chọn tuần bắt đầu từ thứ Hai tuần sau trở đi; lịch phải được giữ chậm nhất vào thứ Sáu trước đó.");
        if (request.TemplateIds.Length == 0 ||
            request.TemplateIds.Distinct().Count() != request.TemplateIds.Length)
            return Invalid<Booking>("Ngày bắt đầu hoặc mẫu lịch không hợp lệ.");
        var member = await User(actor, UserRole.Member, ct) ?? await User(actor, UserRole.Customer, ct);
        if (member is null)
            return Forbidden<Booking>();
        var trainer = await User(request.TrainerId, UserRole.PT, ct);
        if (trainer is null || trainer.GymId != member.GymId || actor == request.TrainerId)
            return Missing<Booking>();
        if (await ActiveYogaAssignments.AnyAsync(store, c => c.InstructorUserId == request.TrainerId, ct))
            return Conflict<Booking>("Huấn luyện viên đang phụ trách lớp Yoga.");
        var profile = await PTProfiles.SingleOrDefaultAsync(store, p => p.PTUserId == request.TrainerId, ct);
        var package = await PTPackages.SingleOrDefaultAsync(store, p => p.Id == request.PackageId &&
            p.PackageStatus == PackageStatus.Active, ct);
        if (profile is null || package is null || package.Components.Single().ApplicableTrainerLevel != profile.Level)
            return Invalid<Booking>("Gói PT hoặc cấp độ huấn luyện viên không hợp lệ.");
        if (!profile.AcceptingNewMembers)
            return Conflict<Booking>("Huấn luyện viên hiện không nhận học viên mới.");
        var terms = package.Components.Single();
        var pattern = await Templates(request.TrainerId).Where(x => request.TemplateIds.Contains(x.Id))
            .OrderBy(x => x.DayOfWeek).ToArrayAsync(store, ct);
        if (pattern.Length != request.TemplateIds.Length || pattern.Select(x => x.DayOfWeek).Distinct().Count() != pattern.Length)
            return Invalid<Booking>("Chọn một ca đã công bố cho mỗi ngày tập.");
        var dates = ProposedDates(request.StartDate, pattern, terms.SessionCount.GetValueOrDefault());
        if (dates.Length == 0)
            return Invalid<Booking>("Không thể xếp đủ số buổi từ ngày bắt đầu đã chọn.");
        return Result<Booking>.Success(new(new(package.Id, request.TrainerId, dates[^1].Date, package.Price, dates),
            package, profile, trainer, pattern));
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

    private async Task<Result<MemberMembership>> ValidateHold(string actor, Booking booking, DateTime now, CancellationToken ct)
    {
        var dates = booking.Preview.Sessions;
        if (dates.Count != booking.Package.Components.Single().SessionCount || dates.Any(s =>
                !InHours(booking.TrainerUser.Gym, s.StartTime, s.EndTime) || Utc(s.Date, s.StartTime) <= now.Add(HoldDuration)))
            return Conflict<MemberMembership>("Không đủ số buổi hợp lệ, có ca ngoài giờ gym hoặc quá gần giờ bắt đầu.");
        var today = DateOnly.FromDateTime(now.AddHours(7));
        var membership = await Coverage(actor).Where(m => m.StartDate <= today && m.EndDate >= today &&
            !m.MembershipFreezes.Any(f => (f.RequestStatus == FreezeRequestStatus.Approved ||
                f.RequestStatus == FreezeRequestStatus.Active || f.RequestStatus == FreezeRequestStatus.Completed) &&
                (f.ActualStartDate ?? f.RequestedStartDate) <= today &&
                (f.ActualEndDate ?? f.RequestedEndDate) > today))
            .OrderByDescending(m => m.EndDate).FirstOrDefaultAsync(store, ct);
        if (membership is null)
            return Result<MemberMembership>.Failure("Cần gói Gym đang hoạt động vào ngày đăng ký gói PT.", "PT_GYM_COVERAGE", 409);
        if (await HasConflict(booking.Preview.TrainerId, actor, dates, now, ct))
            return await HasMemberBookingConflict(actor, dates, now, ct)
                ? Result<MemberMembership>.Failure("Lịch dự kiến trùng lịch PT hoặc Yoga của bạn.", "PT_MEMBER_SCHEDULE_CONFLICT", 409)
                : Result<MemberMembership>.Failure("Ca của PT đã được giữ, đã đặt hoặc trùng lịch Yoga.", "PT_TRAINER_SLOT_UNAVAILABLE", 409);
        return Result<MemberMembership>.Success(membership);
    }

    private async Task<bool> HasMemberBookingConflict(string actor, IReadOnlyList<PTCalendarSlot> slots, DateTime now, CancellationToken ct)
    {
        var dates = slots.Select(s => s.Date).Distinct().ToArray();
        if (await Occupied(now).AnyAsync(store, s => s.MemberUserId == actor && dates.Contains(s.SessionDate), ct))
            return true;
        var yoga = await Yoga(string.Empty, actor, dates.Min(), dates.Max())
            .Select(s => new { s.ScheduledDate, s.StartTime, s.EndTime }).ToListAsync(store, ct);
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
            return Invalid<PTWeeklySlot[]>("Chọn tuần bắt đầu từ thứ Hai tuần sau trở đi; lịch phải được giữ chậm nhất vào thứ Sáu trước đó.");
        var member = await User(actor, UserRole.Member, ct) ?? await User(actor, UserRole.Customer, ct);
        if (member is null)
            return Forbidden<PTWeeklySlot[]>();
        var trainer = await User(request.TrainerId, UserRole.PT, ct);
        if (trainer is null || trainer.GymId != member.GymId || actor == request.TrainerId)
            return Missing<PTWeeklySlot[]>();

        var level = await PTProfiles.Where(p => p.PTUserId == request.TrainerId)
            .Select(p => (TrainerLevel?)p.Level).SingleOrDefaultAsync(store, ct);
        var package = await PTPackages.SingleOrDefaultAsync(store, p => p.Id == request.PackageId &&
            p.PackageStatus == PackageStatus.Active, ct);
        var component = package?.Components.Single();
        if (level is null || component is null || component.ApplicableTrainerLevel != level)
            return Invalid<PTWeeklySlot[]>("Gói PT hoặc cấp độ huấn luyện viên không hợp lệ.");

        var templates = await Templates(request.TrainerId).AsNoTracking(store).OrderBy(s => s.StartTime).ToArrayAsync(store, ct);
        if (templates.Length == 0)
            return Result<PTWeeklySlot[]>.Success([]);
        var selected = templates.Where(s => request.TemplateIds.Contains(s.Id))
            .DistinctBy(s => s.DayOfWeek).ToArray();
        var patterns = templates.ToDictionary(s => s.Id, s => ProposedDates(request.StartDate,
            selected.Where(item => item.DayOfWeek != s.DayOfWeek).Append(s).ToArray(), component.SessionCount.GetValueOrDefault()));
        var dates = patterns.Values.SelectMany(slots => slots).Distinct().ToArray();
        var now = Now;
        var conflicts = await ConflictingSlots(request.TrainerId, actor, dates, now, ct);
        var through = dates.Length == 0 ? request.StartDate : dates.Max(s => s.Date);
        var trainerBusy = await Occupied(now).Where(s => s.PTUserId == request.TrainerId &&
                s.SessionDate >= request.StartDate && s.SessionDate <= through)
            .Select(s => new { s.SessionDate, s.StartTime, s.EndTime, s.SessionStatus })
            .ToListAsync(store, ct);
        var yogaAssigned = await ActiveYogaAssignments.AnyAsync(store, c => c.InstructorUserId == request.TrainerId, ct);

        return Result<PTWeeklySlot[]>.Success(templates.Select(slot =>
        {
            var pattern = patterns[slot.Id];
            var proposed = pattern.Where(s => s.TemplateId == slot.Id).ToArray();
            var reason = yogaAssigned || pattern.Length == 0 || proposed.Any(s =>
                !InHours(trainer.Gym, s.StartTime, s.EndTime) || Utc(s.Date, s.StartTime) <= now.Add(HoldDuration))
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
                MemberMembershipId = eligible.Data!.Id,
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
            store.Add(purchase);
            await store.Save(ct);
            purchase.HoldExpiresAt = order.CreatedAt.Add(HoldDuration);
            await store.Save(ct);
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
            var purchase = await store.Query<PTPurchase>().SingleOrDefaultAsync(store, p => p.OrderId == order.Id && p.MemberUserId == actor, ct);
            if (purchase is null)
                return Missing<PTCheckoutResponse>();
            var now = Now;
            if (purchase.Status != PTPurchaseStatus.PendingPayment || PaymentDeadline(order) <= now ||
                order.OrderStatus != OrderStatus.Pending)
                return Conflict<PTCheckoutResponse>("Đơn PT đã hết hạn hoặc đã được xử lý.");

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
            if (!string.IsNullOrWhiteSpace(request.VoucherCode))
            {
                voucher = await ReserveVoucher(actor, request.VoucherCode, purchase.PurchasePrice, now, ct);
                if (voucher is null)
                    return Result<PTCheckoutResponse>.Failure("Voucher không hợp lệ hoặc đã hết lượt.", "PT_VOUCHER_INVALID");
            }
            var due = purchase.PurchasePrice - (voucher?.DiscountAmount ?? 0);
            if (request.PaymentMethod == MembershipPaymentMethods.Cash)
            {
                // The payment row is created when Staff confirms the cash, so SePay and membership cash confirmation never match it.
                order.Channel = OrderChannel.Counter;
                order.VoucherRedemption = voucher;
                purchase.PurchasePrice = due;
                purchase.HoldExpiresAt = order.CreatedAt.Add(CounterHold);
                await store.Save(ct);
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
            await store.Save(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(purchase, order, qr, now));
        }, ct);

    public Task<Result<PTCheckoutResponse>> ConfirmDemoPayment(string actor, string orderNumber, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Member, ct) is null)
                return Forbidden<PTCheckoutResponse>();
            var order = await payments.FindOrderAsync(orderNumber, actor, forUpdate: true, ct);
            if (order is null)
                return Missing<PTCheckoutResponse>();
            var contract = await store.Query<PTPurchase>().Including(store, "Sessions")
                .SingleOrDefaultAsync(store, p => p.OrderId == order.Id && p.MemberUserId == actor, ct);
            if (contract is null)
                return Missing<PTCheckoutResponse>();

            var now = clock.GetUtcNow();
            if (!CanSimulatePayment(contract, order, now.UtcDateTime))
                return Conflict<PTCheckoutResponse>("Đơn đã hết hạn, đã tạo VietQR hoặc đã được xử lý.");
            if (!await HeldSlotsStillValid(contract, now.UtcDateTime, ct))
                return Conflict<PTCheckoutResponse>("Lịch PT không còn hợp lệ.");

            order.PaymentTransaction = new PaymentTransaction
            {
                UserId = actor,
                GatewayName = DemoGateway,
                PaymentMethod = PaymentMethod.BankTransfer,
                AmountDue = contract.PurchasePrice,
                RawGatewayStatus = "DEMO"
            };
            MembershipPurchaseService.RecordPaidOrder(order, contract.PurchasePrice, now, null, now);
            ActivatePTContract(contract);
            Audit(actor, nameof(PTPurchase), contract.Id, "DemoPaymentConfirmed");
            await store.Save(ct);
            return Result<PTCheckoutResponse>.Success(CheckoutResponse(contract, order, null, now.UtcDateTime));
        }, ct);

    public Task<Result<PTCheckoutResponse>> ConfirmCash(string staffId, string orderNumber, decimal amount, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await User(staffId, UserRole.Staff, ct);
            if (staff is null)
                return Result<PTCheckoutResponse>.Failure("Cần quyền nhân viên lễ tân.", "CASH_CONFIRMATION_FORBIDDEN", 403);
            var order = await payments.FindOrderAsync(orderNumber, purchaserId: null, forUpdate: true, ct);
            var contract = order is null || order.Purchaser.GymId != staff.GymId ? null
                : await store.Query<PTPurchase>().Including(store, "Sessions").SingleOrDefaultAsync(store, p => p.OrderId == order.Id, ct);
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
            await store.Save(ct);
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
            !await ActiveYogaAssignments.AnyAsync(store, c => c.InstructorUserId == contract.PTUserId, ct) &&
            !await HasConflict(contract.PTUserId, contract.MemberUserId, slots, now, ct, ignorePurchase: contract.Id);
    }

    private static bool CanSimulatePayment(PTPurchase contract, Order order, DateTime now) =>
        contract.Status == PTPurchaseStatus.PendingPayment && contract.HoldExpiresAt > now &&
        order.OrderStatus == OrderStatus.Pending && order.PaymentTransaction is null && PaymentDeadline(order) > now;

    public async Task<Result<PTCheckoutResponse>> CheckoutStatus(string actor, string orderNumber, CancellationToken ct,
        bool demoPaymentEnabled = false)
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTCheckoutResponse>();
        var purchase = await store.Query<PTPurchase>().Including(store, "Order.PaymentTransaction.Invoice")
            .SingleOrDefaultAsync(store, p => p.MemberUserId == actor && p.Order!.OrderNumber == orderNumber, ct);
        if (purchase?.Order is not { } order)
            return Missing<PTCheckoutResponse>();
        var now = Now;
        var qr = order.OrderStatus == OrderStatus.Pending && PaymentDeadline(order) > now &&
            order.PaymentTransaction is { } payment ? gateway.CreateVietQr(order.OrderNumber, payment.AmountDue) : null;
        var response = CheckoutResponse(purchase, order, qr, now) with
        {
            CanSimulatePayment = demoPaymentEnabled && CanSimulatePayment(purchase, order, now)
        };
        if (order.PaymentTransaction is not { } original)
            return Result<PTCheckoutResponse>.Success(response);
        var refunds = await store.Query<PaymentTransaction>().AsNoTracking(store)
            .Where(payment => (payment.Id == original.Id || payment.SourcePaymentTransactionId == original.Id) &&
                payment.RefundStatus != null && payment.RefundAmount > 0 && payment.PaidAt != null &&
                payment.GatewayTransactionReference != null)
            .OrderByDescending(payment => payment.Id).ToListAsync(store, ct);
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
            IsDemoPayment: order.PaymentTransaction?.GatewayName == DemoGateway,
            ProceedBefore: DateTime.SpecifyKind(order.CreatedAt.Add(ProceedWindow), DateTimeKind.Utc),
            PayAtCounter: order.Channel == OrderChannel.Counter);

    private async Task<VoucherRedemption?> ReserveVoucher(string actor, string code, decimal price, DateTime now, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var voucher = await store.Query<Voucher>().SingleOrDefaultAsync(store, v => v.Code.ToUpper() == normalized &&
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
            VoucherId = voucher.Id,
            DiscountAmount = discount,
            RedeemedAt = now
        } : null;
    }

    private async Task<bool> VoucherHasCapacity(Voucher voucher, string actor, CancellationToken ct, int? excludingOrder = null)
    {
        var active = store.Query<VoucherRedemption>().Where(r => r.VoucherId == voucher.Id &&
            (excludingOrder == null || r.OrderId != excludingOrder) &&
            (r.RedemptionStatus == VoucherRedemptionStatus.Confirmed ||
             r.RedemptionStatus == VoucherRedemptionStatus.Reserved && r.Order.OrderStatus == OrderStatus.Pending));
        if (voucher.MaxRedemptions is { } max && await active.CountAsync(store, ct) >= max)
            return false;
        return voucher.MaxRedemptionsPerUser is not { } perUser ||
            await active.CountAsync(store, r => r.Order.PurchaserUserId == actor, ct) < perUser;
    }

    public async Task<Result<SepayWebhookResponse>?> TryFulfillAsync(Order order, SepayTransfer transfer, CancellationToken ct)
    {
        var contract = await store.Query<PTPurchase>().Including(store, "Sessions").SingleOrDefaultAsync(store, p => p.OrderId == order.Id, ct);
        if (contract is null)
            return null;
        var payment = order.PaymentTransaction!;
        var now = clock.GetUtcNow();
        if (await store.IsTransferRecorded(payment, transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Success(new(true));
        var created = new DateTimeOffset(DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc));
        if (transfer.TransferredAt.ToUnixTimeSeconds() < created.ToUnixTimeSeconds() ||
            await payments.SepayReferenceUsedAsync(transfer.TransactionId, ct))
            return Result<SepayWebhookResponse>.Failure("Không thể xác nhận thanh toán PT.", "PT_PAYMENT_REJECTED", 409);
        if (payment.GatewayTransactionReference is not null || order.OrderStatus == OrderStatus.Paid)
        {
            store.RecordExtraTransfer(payment, transfer, now.UtcDateTime);
            await store.Save(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        var slots = contract.Sessions.Select(s => new PTCalendarSlot(s.PTAvailabilityId ?? 0, s.SessionDate,
            s.StartTime, s.EndTime, "Available")).ToArray();
        var trainer = await User(contract.PTUserId, UserRole.PT, ct);
        var redemption = await store.Query<VoucherRedemption>().Including(store, "Voucher")
            .SingleOrDefaultAsync(store, r => r.OrderId == order.Id, ct);
        if (contract.Status is not (PTPurchaseStatus.PendingPayment or PTPurchaseStatus.Expired) ||
            order.OrderStatus is not (OrderStatus.Pending or OrderStatus.Expired) ||
            payment.AmountDue != transfer.Amount || transfer.TransferredAt.UtcDateTime >= PaymentDeadline(order) ||
            slots.Length != contract.TotalSessions || slots.Length == 0 ||
            trainer is null || slots.Any(s => Utc(s.Date, s.StartTime) <= now.UtcDateTime ||
                !InHours(trainer.Gym, s.StartTime, s.EndTime)) ||
            (contract.HoldExpiresAt <= now.UtcDateTime && !await OriginalTemplatesAvailable(contract.PTUserId, slots, ct)) ||
            (redemption?.RedemptionStatus == VoucherRedemptionStatus.Released &&
                !await VoucherHasCapacity(redemption.Voucher, contract.MemberUserId, ct, order.Id)) ||
            await ActiveYogaAssignments.AnyAsync(store, c => c.InstructorUserId == contract.PTUserId, ct) ||
            await HasConflict(contract.PTUserId, contract.MemberUserId, slots, now.UtcDateTime, ct, ignorePurchase: contract.Id))
        {
            store.RecordUnappliedTransfer(payment, transfer, now.UtcDateTime);
            contract.Status = PTPurchaseStatus.Expired;
            foreach (var session in contract.Sessions)
                session.SessionStatus = PTSessionStatus.Cancelled;
            if (redemption is not null)
                redemption.RedemptionStatus = VoucherRedemptionStatus.Released;
            Audit(null, nameof(PTPurchase), contract.Id, "PT_REFUND_REQUESTED");
            Notify(contract.MemberUserId, "Thanh toán PT chưa được áp dụng",
                $"Đã nhận {transfer.Amount:N0} VND cho đơn {order.OrderNumber} nhưng không thể xác nhận lịch tập. " +
                "Vui lòng liên hệ quầy lễ tân để được hoàn tiền.", nameof(PTPurchase), contract.Id);
            await store.Save(ct);
            return Result<SepayWebhookResponse>.Success(new(true));
        }

        MembershipPurchaseService.RecordPaidOrder(order, transfer.Amount, transfer.TransferredAt, transfer.TransactionId, now);
        ActivatePTContract(contract);
        if (redemption is not null)
            redemption.RedemptionStatus = VoucherRedemptionStatus.Confirmed;
        Notify(contract.MemberUserId, "Đã thanh toán gói PT", $"Hóa đơn {payment.Invoice!.InvoiceNumber}. Lịch tập đã được xác nhận.",
            nameof(PTPurchase), contract.Id);
        Audit(null, nameof(PTPurchase), contract.Id, nameof(PTPurchaseStatus.Active));
        await store.Save(ct);
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
        var template = await Templates(trainerId).Select(s => new { s.DayOfWeek, s.StartTime, s.EndTime }).ToListAsync(store, ct);
        return slots.All(slot => template.Any(s => s.DayOfWeek == slot.Date.DayOfWeek &&
            s.StartTime == slot.StartTime && s.EndTime == slot.EndTime));
    }
}
