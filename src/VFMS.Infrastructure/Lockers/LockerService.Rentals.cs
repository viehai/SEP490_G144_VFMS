using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Lockers;

public sealed partial class LockerService
{
    public Task<Result<LockerRentalView>> Handover(string actor, int id, LockerHandoverRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            var staff = await Actor(actor, UserRole.Staff, ct);
            if (staff is null)
                return Forbidden<LockerRentalView>();
            var rental = await Rentals.SingleOrDefaultAsync(r => r.Id == id && r.Locker.LockerZone.GymId == staff.GymId, ct);
            if (rental is null)
                return Missing<LockerRentalView>();
            var current = Now;
            var now = current.UtcDateTime;
            if (rental.HandedOverAt is not null)
                return Result<LockerRentalView>.Success(View(rental, now));
            if (!request.TermsAccepted || !request.IdentityVerified)
                return Invalid<LockerRentalView>("Cần xác minh hội viên, xác nhận đồng ý điều khoản và giao chìa.");
            var payment = rental.Payments.SingleOrDefault(p => p.LockerPurpose == LockerPaymentPurpose.Rental);
            var collectCash = request.CashAmount is not null && rental.ContractStatus == LockerContractStatus.PendingPayment;
            if (payment is null || rental.ExpiresAt <= now ||
                rental.Locker.LockerStatus != LockerStatus.Reserved ||
                !await Eligible(rental.CustomerUserId, now, ct))
                return Conflict<LockerRentalView>("Hợp đồng hoặc hội viên không còn đủ điều kiện nhận chìa.");

            if (collectCash)
            {
                if (payment.Order.Channel != OrderChannel.Counter || payment.PaymentMethod != PaymentMethod.Cash ||
                    request.CashAmount != payment.AmountDue || !Pending(payment, now) ||
                    !await CanFulfill(payment, now, now, ct))
                    return Conflict<LockerRentalView>("Kiểm tra phương thức, số tiền và thời hạn của đơn tại quầy.");
            }
            else if (rental.ContractStatus != LockerContractStatus.PendingHandover ||
                payment.PaymentStatus != PaymentStatus.Paid || payment.PaidAt is null)
                return Conflict<LockerRentalView>("Đơn chưa thanh toán.");

            var today = LocalDate(now);
            var paidAt = collectCash ? now : payment.PaidAt!.Value;
            if (Type(rental) == RentalType.Daily &&
                (today != LocalDate(paidAt) || !OpenForDailyRental(rental.Locker.LockerZone.Gym, now)))
                return Conflict<LockerRentalView>("Chỉ giao chìa thuê ngày trong giờ mở cửa của ngày đã thanh toán.");
            if (collectCash)
                await ApplyPayment(payment, payment.AmountDue, current, null, current, actor, ct);
            if (Type(rental) == RentalType.Daily)
            {
                var gym = rental.Locker.LockerZone.Gym;
                rental.StartDate = today;
                rental.EndDate = today;
                rental.ExpiresAt = today.ToDateTime(gym.CloseTime!.Value, DateTimeKind.Utc).Subtract(LocalOffset);
            }
            else
            {
                var latestStart = LocalDate(paidAt).AddDays(2);
                var start = payment.Order.Channel == OrderChannel.Online && latestStart < today ? latestStart : today;
                SetMonthlyTerm(rental, start, Months(Type(rental)));
            }
            rental.HandedOverAt = now;
            rental.HandedOverByUserId = actor;
            rental.ContractStatus = LockerContractStatus.Active;
            rental.Locker.LockerStatus = LockerStatus.Rented;
            Audit(actor, rental, "LockerKeyHandedOver:IdentityVerified:TermsAccepted", now);
            await db.SaveChangesAsync(ct);
            return Result<LockerRentalView>.Success(View(rental, now));
        }, ct);

    public Task<Result<LockerCheckout>> Renew(string actor, UserRole role, int id, LockerRenewalRequest request,
        CancellationToken ct) => Write(async () =>
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerCheckout>();
        var method = role == UserRole.Member ? PaymentMethod.VietQR : ParseCollection(request.PaymentMethod);
        if (method is null)
            return Invalid<LockerCheckout>("Chọn Cash hoặc VietQR.");
        var rental = await Rentals.SingleOrDefaultAsync(r => r.Id == id && r.Locker.LockerZone.GymId == user.GymId &&
            (role != UserRole.Member || r.CustomerUserId == actor), ct);
        if (rental is null)
            return Missing<LockerCheckout>();
        var now = Now.UtcDateTime;
        if (!InUse(rental) || OverdueDays(rental, now) > 7 || Type(rental) == RentalType.Daily ||
            rental.Locker.LockerStatus is LockerStatus.Maintenance or LockerStatus.Damaged ||
            !await Eligible(rental.CustomerUserId, now, ct))
            return Conflict<LockerCheckout>("Hợp đồng không đủ điều kiện gia hạn.");
        if (rental.Payments.Any(p => Pending(p, now) ||
            p.LockerPurpose == LockerPaymentPurpose.Settlement && p.Order.OrderStatus == OrderStatus.Paid))
            return Conflict<LockerCheckout>("Hợp đồng đang có khoản thu gia hạn hoặc tất toán cần xử lý.");
        var policy = await db.LockerPricingPolicies.AsNoTracking().SingleOrDefaultAsync(p =>
            p.Id == request.PolicyId && p.PolicyStatus == PolicyStatus.Active, ct);
        if (policy is null || Months(policy.RentalType) == 0 || !ValidPolicy(policy))
            return Invalid<LockerCheckout>("Chọn một gói thuê theo tháng.");
        var overdue = OverdueFees(rental, now);
        var charge = BuildCharge(rental, LockerPaymentPurpose.Renewal, method.Value,
            role == UserRole.Member ? OrderChannel.Online : OrderChannel.Counter, policy.RentalPrice + overdue, now);
        charge.RenewalMonths = Months(policy.RentalType);
        charge.RentalExpiryAtQuote = RentalExpiry(rental);
        charge.OverdueFees = overdue;
        var qr = Qr(charge);
        if (method == PaymentMethod.VietQR && qr is null)
            return PaymentUnavailable();
        db.PaymentTransactions.Add(charge);
        await db.SaveChangesAsync(ct);
        return Result<LockerCheckout>.Success(Checkout(charge, now, qr), 201);
    }, ct);

    private static bool Money(decimal value, bool zeroAllowed = false) =>
        value >= (zeroAllowed ? 0 : 1) && value <= 1_000_000_000_000m && decimal.Truncate(value) == value;

    private static bool ValidPolicy(LockerPricingPolicy policy) =>
        Money(policy.RentalPrice) && Money(policy.KeyReplacementFee, true) && Money(policy.OverdueDailyFee, true) &&
        (policy.RentalType == RentalType.Daily ? Money(policy.DepositAmount) : Months(policy.RentalType) > 0 && policy.DepositAmount == 0);

    public Task<Result<IReadOnlyList<LockerPolicy>>> SavePricing(string actor, LockerPricingRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            if (await Actor(actor, UserRole.Manager, ct) is null)
                return Forbidden<IReadOnlyList<LockerPolicy>>();
            var tiers = new (RentalType Type, string Name, decimal Price, decimal Deposit)[]
            {
                (RentalType.Daily, "Thuê ngày", input.DailyFee, input.DailyDeposit),
                (RentalType.Monthly, "1 tháng", input.OneMonth, 0),
                (RentalType.Quarterly, "3 tháng", input.ThreeMonths, 0),
                (RentalType.HalfYearly, "6 tháng", input.SixMonths, 0),
                (RentalType.Yearly, "12 tháng", input.TwelveMonths, 0)
            };
            if (tiers.Any(p => !Money(p.Price)) || !Money(input.DailyDeposit) ||
                !Money(input.OverdueDailyFee, true) || !Money(input.KeyReplacementFee, true) ||
                input.ThreeMonths >= input.OneMonth * 3 || input.SixMonths >= input.ThreeMonths * 2 ||
                input.TwelveMonths >= input.SixMonths * 2)
                return Invalid<IReadOnlyList<LockerPolicy>>("Kiểm tra số tiền và chiết khấu lũy tiến theo kỳ hạn.");

            var current = await db.LockerPricingPolicies.Where(p => p.PolicyStatus == PolicyStatus.Active).ToListAsync(ct);
            foreach (var previous in current)
                previous.PolicyStatus = PolicyStatus.Inactive;
            var next = tiers.Select(t => new LockerPricingPolicy
            {
                RentalType = t.Type,
                PolicyName = t.Name,
                RentalPrice = t.Price,
                DepositAmount = t.Deposit,
                OverdueDailyFee = t.Type == RentalType.Daily ? 0 : input.OverdueDailyFee,
                KeyReplacementFee = input.KeyReplacementFee
            }).ToArray();
            db.LockerPricingPolicies.AddRange(next);
            await db.SaveChangesAsync(ct);
            return Result<IReadOnlyList<LockerPolicy>>.Success(next.Select(Policy).ToArray());
        }, ct);

    public async Task<Result<IReadOnlyList<LockerMember>>> FindMembers(string actor, string? search, CancellationToken ct)
    {
        var staff = await Actor(actor, UserRole.Staff, ct);
        if (staff is null)
            return Forbidden<IReadOnlyList<LockerMember>>();
        search = search?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length > 100)
            return Invalid<IReadOnlyList<LockerMember>>("Nhập tên hoặc số điện thoại để tìm hội viên.");
        var now = Now.UtcDateTime;
        var eligible = EligibleMemberships(now);
        var allocated = Allocations(db.LockerRentalContracts, now);
        var users = await Users(UserRole.Member).Where(u => u.GymId == staff.GymId &&
                (u.FullName.Contains(search) || u.PhoneNumber.Contains(search)))
            .OrderBy(u => u.FullName).ThenBy(u => u.UserId).Take(25)
            .Select(u => new LockerMember(u.UserId, u.FullName, u.PhoneNumber,
                eligible.Any(m => m.MemberUserId == u.UserId) && !allocated.Any(r => r.CustomerUserId == u.UserId)))
            .ToListAsync(ct);
        return Result<IReadOnlyList<LockerMember>>.Success(users);
    }

    public async Task<Result<IReadOnlyList<LockerStaffRental>>> Registry(
        string actor, UserRole role, string? search, bool overdueOnly, CancellationToken ct)
    {
        var staff = await Actor(actor, role, ct);
        if (staff is null)
            return Forbidden<IReadOnlyList<LockerStaffRental>>();
        search = search?.Trim();
        if (search?.Length > 100)
            return Invalid<IReadOnlyList<LockerStaffRental>>("Từ khóa tìm kiếm tối đa 100 ký tự.");
        var now = Now.UtcDateTime;
        var today = LocalDate(now);
        var query = Allocations(Rentals.AsNoTracking(), now)
            .Where(r => r.Locker.LockerZone.GymId == staff.GymId &&
                (overdueOnly || r.ContractStatus != LockerContractStatus.PendingPayment));
        if (overdueOnly)
            query = query.Where(r => r.ContractStatus != LockerContractStatus.PendingPayment &&
                (r.ExpiresAt <= now || r.ContractStatus != LockerContractStatus.PendingHandover &&
                    r.ExpiresAt == null && r.EndDate < today));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.ContractNumber.Contains(search) || r.Locker.LockerNumber.Contains(search) ||
                r.Customer.FullName.Contains(search) || r.Payments.Any(p => p.LockerPurpose == LockerPaymentPurpose.Rental &&
                    p.Invoice!.InvoiceNumber.Contains(search)));
        var data = await query.OrderBy(r => r.EndDate).ThenBy(r => r.Id).Take(100).ToListAsync(ct);
        return Result<IReadOnlyList<LockerStaffRental>>.Success(data.Select(r =>
            new LockerStaffRental(View(r, now), r.CustomerUserId, r.Customer.FullName, r.Customer.PhoneNumber)).ToArray());
    }

    public async Task<Result<LockerStaffRental>> Details(string actor, UserRole role, int id, CancellationToken ct)
    {
        var user = await Actor(actor, role, ct);
        if (user is null)
            return Forbidden<LockerStaffRental>();
        var rental = await StaffRental(user, id, ct);
        return rental is null ? Missing<LockerStaffRental>() : Result<LockerStaffRental>.Success(
            new(View(rental, Now.UtcDateTime), rental.CustomerUserId, rental.Customer.FullName, rental.Customer.PhoneNumber));
    }
}
