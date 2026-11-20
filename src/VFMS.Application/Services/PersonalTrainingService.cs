using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService(IPersonalTrainingStore store, TimeProvider clock,
    IMembershipPaymentGateway gateway, IMembershipPurchaseRepository payments, IBannedWordService bannedWords)
    : IPTPaymentFulfillment
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now.AddHours(7));
    private static DateTime Utc(DateOnly date, TimeOnly time) =>
        date.ToDateTime(time, DateTimeKind.Utc).AddHours(-7);

    private Task<Result<T>> Write<T>(Func<Task<Result<T>>> work, CancellationToken ct) => store.Write(work, ct);

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, "PT_VALIDATION");
    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, "PT_CONFLICT", 409);
    private static Result<T> Missing<T>() => Result<T>.Failure("Không tìm thấy dữ liệu PT.", "PT_NOT_FOUND", 404);
    private static Result<T> Forbidden<T>() => Result<T>.Failure("Tài khoản không có quyền thực hiện.", "PT_FORBIDDEN", 403);

    private IQueryable<ApplicationUser> ActiveUsers => store.Query<ApplicationUser>()
        .Where(u => u.AccountStatus == AccountStatus.Active && !u.IsBanned);

    private Task<ApplicationUser?> User(string id, UserRole role, CancellationToken ct) =>
        store.Single(store.Include(ActiveUsers, nameof(ApplicationUser.Gym)).Where(u => u.UserId == id &&
            u.UserRoles.Any(r => r.Role.RoleName == role.ToString())), ct);

    private IQueryable<YogaClass> ActiveYogaAssignments => store.Query<YogaClass>().Where(c =>
        !c.IsDeleted && (c.ClassStatus == YogaClassStatus.Draft || c.ClassStatus == YogaClassStatus.Published));

    // Feature 4 serves trainers whose Trainer Role is PT only; Yoga trainers never sell or take over PT contracts.
    private IQueryable<TrainerProfile> PTProfiles => store.Query<TrainerProfile>()
        .Where(p => p.IsActive && p.TrainerRole!.RoleName == TrainerRole.PT);

    private IQueryable<PTAvailability> Templates(string trainerId) => store.Query<PTAvailability>()
        .Where(x => x.PTUserId == trainerId && x.DayOfWeek != null && x.SlotStatus == SlotStatus.Available);

    private IQueryable<PTSession> Occupied(DateTime now) =>
        store.Query<PTSession>().Where(PersonalTrainingRules.OccupiesTime(now));

    private static bool InHours(Gym gym, TimeOnly start, TimeOnly end) =>
        gym.OpenTime is { } open && gym.CloseTime is { } close && start >= open && end <= close && end > start;

    private static bool IsWholeHour(TimeOnly time) => time.Ticks % TimeSpan.TicksPerHour == 0;

    private IQueryable<MemberMembership> Coverage(string memberId) => store.Query<MemberMembership>().Where(x =>
        x.MemberUserId == memberId && x.MembershipStatus == MembershipStatus.Active &&
        x.PackageComponent.ComponentType == ComponentType.GYM);

    private async Task<bool> HasConflict(string trainerId, string memberId,
        IReadOnlyList<PTCalendarSlot> dates, DateTime now, CancellationToken ct, int? ignoreSession = null,
        int? ignorePurchase = null, bool allowSecondMemberSession = false) =>
        (await ConflictingSlots(trainerId, memberId, dates, now, ct, ignoreSession, ignorePurchase,
            allowSecondMemberSession)).Count != 0;

    private async Task<HashSet<PTCalendarSlot>> ConflictingSlots(string trainerId, string memberId,
        IReadOnlyList<PTCalendarSlot> dates, DateTime now, CancellationToken ct, int? ignoreSession = null,
        int? ignorePurchase = null, bool allowSecondMemberSession = false)
    {
        if (dates.Count == 0)
            return [];
        var from = dates.Min(s => s.Date);
        var through = dates.Max(s => s.Date);
        var pt = await Occupied(now).Where(s => s.SessionDate >= from && s.SessionDate <= through &&
            (s.PTUserId == trainerId || s.MemberUserId == memberId) &&
            (ignoreSession == null || s.Id != ignoreSession) &&
            (ignorePurchase == null || s.PTPurchaseId != ignorePurchase))
            .Select(s => new { s.SessionDate, s.StartTime, s.EndTime, s.MemberUserId }).ToListAsync(store, ct);
        var yoga = await Yoga(trainerId, memberId, from, through)
            .Select(s => new { s.ScheduledDate, s.StartTime, s.EndTime }).ToListAsync(store, ct);
        return dates.Where(d => pt.Any(s => s.SessionDate == d.Date &&
            (!allowSecondMemberSession && s.MemberUserId == memberId ||
             PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime))) ||
            yoga.Any(s => s.ScheduledDate == d.Date &&
             PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime))).ToHashSet();
    }

    private IQueryable<YogaClassSchedule> Yoga(string trainerId, string? memberId, DateOnly from, DateOnly through) =>
        store.Query<YogaClassSchedule>().Where(s => s.ScheduledDate >= from && s.ScheduledDate <= through &&
            (s.ScheduleStatus == YogaScheduleStatus.Scheduled || s.ScheduleStatus == YogaScheduleStatus.Ongoing) &&
            (s.YogaClass.InstructorUserId == trainerId || memberId != null &&
                s.YogaClass.Bookings.Any(b => b.MemberUserId == memberId && b.BookingStatus == YogaBookingStatus.Confirmed &&
                    (b.StartDate == null || b.StartDate <= s.ScheduledDate) &&
                    (b.EndDate == null || b.EndDate >= s.ScheduledDate))));

    private void Audit(string? actor, string entity, int id, string value) =>
        store.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = entity,
            EntityId = id,
            NewValue = value,
            OccurredAt = Now
        });

    private void Notify(string recipient, string title, string message, string entity, int id) =>
        store.Add(new Notification
        {
            RecipientUserId = recipient,
            Title = title,
            Message = message,
            NotificationType = "PersonalTraining",
            EntityType = entity,
            EntityId = id,
            NotifiedAt = Now
        });

    /// <summary>GBR-98: removes the PT role after more than 60 consecutive 'Not Accepting' days, once no contract is still running.</summary>
    public async Task ExpireInactiveTrainers(DateOnly today, CancellationToken ct)
    {
        var profiles = await store.List(store.Query<TrainerProfile>().Where(p => p.IsActive && !p.AcceptingNewMembers &&
            p.NotAcceptingSince != null), ct);
        var ptRoleId = await store.Single(store.Query<ApplicationRole>().Where(role => role.RoleName == nameof(UserRole.PT))
            .Select(role => (int?)role.Id), ct);
        foreach (var profile in profiles)
        {
            if (!TrainerReadinessRules.HasExpired(profile.NotAcceptingSince, today) ||
                await store.Query<PTPurchase>().AnyAsync(store, p => p.PTUserId == profile.PTUserId && !p.IsCompleted &&
                    (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen) && p.EndDate >= today, ct))
                continue;
            profile.IsActive = false;
            if (ptRoleId is { } roleId)
            {
                var assignment = await store.Single(store.Query<ApplicationUserRole>().Where(role =>
                    role.UserId == profile.PTUserId && role.RoleId == roleId), ct);
                if (assignment != null) store.Remove(assignment);
            }
            Audit(profile.PTUserId, nameof(TrainerProfile), profile.Id, "PTInactivityExpired");
            Notify(profile.PTUserId, "Quyền PT đã kết thúc",
                "Bạn đã ở trạng thái không nhận học viên mới quá 60 ngày liên tục và không còn hợp đồng đang chạy.",
                nameof(TrainerProfile), profile.Id);
        }
        await store.Save(ct);
    }

    public async Task ExpirePending(CancellationToken ct)
    {
        var now = Now;
        await Write(async () =>
        {
            var expired = await store.List(store.Include(store.Query<PTPurchase>(), "Sessions",
                    "Order.PaymentTransaction", "Order.VoucherRedemption").Where(p =>
                    p.Status == PTPurchaseStatus.PendingPayment && p.HoldExpiresAt <= now)
                .OrderBy(p => p.Id).Take(100), ct);
            foreach (var contract in expired)
            {
                contract.Status = PTPurchaseStatus.Expired;
                if (contract.Order is { OrderStatus: OrderStatus.Pending } order)
                {
                    order.OrderStatus = OrderStatus.Expired;
                    if (order.PaymentTransaction is { PaymentStatus: PaymentStatus.Pending } payment)
                        payment.PaymentStatus = PaymentStatus.Cancelled;
                    if (order.VoucherRedemption is { RedemptionStatus: VoucherRedemptionStatus.Reserved } voucher)
                        voucher.RedemptionStatus = VoucherRedemptionStatus.Released;
                }
                foreach (var session in contract.Sessions.Where(s => s.SessionStatus == PTSessionStatus.CheckoutHold))
                    session.SessionStatus = PTSessionStatus.Cancelled;
            }

            var proposals = await store.List(store.Query<PTSession>().Where(s =>
                (s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.MakeUpPending ||
                    s.SessionStatus == PTSessionStatus.MakeUpRequested || s.SessionStatus == PTSessionStatus.RescheduleRequested) &&
                s.ProposalExpiresAt <= now).OrderBy(s => s.Id).Take(100), ct);
            foreach (var session in proposals)
            {
                var isMakeUp = session.SessionStatus is PTSessionStatus.MakeUpPending or PTSessionStatus.MakeUpRequested;
                if (session.SessionStatus == PTSessionStatus.RescheduleRequested)
                    CloseMemberReschedule(session, null, "RescheduleExpired", "Yêu cầu đổi lịch đã hết hạn. Lịch cũ được giữ nguyên.");
                else
                    ClearProposal(session);
                if (isMakeUp)
                    session.SessionStatus = PTSessionStatus.Cancelled;
            }
            await store.Save(ct);
            return Result<bool>.Success(true);
        }, ct);
    }
}
