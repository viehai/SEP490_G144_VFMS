using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.PersonalTraining;

public sealed partial class PersonalTrainingService(VfmsDbContext db, TimeProvider clock,
    IMembershipPaymentGateway gateway, IMembershipPurchaseRepository payments, IBannedWordService bannedWords)
    : IPTPaymentFulfillment
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now.AddHours(7));
    private static DateTime Utc(DateOnly date, TimeOnly time) =>
        date.ToDateTime(time, DateTimeKind.Utc).AddHours(-7);

    private async Task<Result<T>> Write<T>(Func<Task<Result<T>>> work, CancellationToken ct)
    {
        try
        {
            return await payments.ExecuteInSerializableTransactionAsync(work, ct);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            return Conflict<T>("Lịch vừa thay đổi. Vui lòng tải lại và thử lại.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
        {
            return Conflict<T>("Dữ liệu vừa thay đổi. Vui lòng tải lại và thử lại.");
        }
    }

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, "PT_VALIDATION");
    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, "PT_CONFLICT", 409);
    private static Result<T> Missing<T>() => Result<T>.Failure("Không tìm thấy dữ liệu PT.", "PT_NOT_FOUND", 404);
    private static Result<T> Forbidden<T>() => Result<T>.Failure("Tài khoản không có quyền thực hiện.", "PT_FORBIDDEN", 403);

    private IQueryable<ApplicationUser> ActiveUsers => db.ApplicationUsers
        .Where(u => u.AccountStatus == AccountStatus.Active && !u.IsBanned);

    private Task<ApplicationUser?> User(string id, UserRole role, CancellationToken ct) =>
        ActiveUsers.Include(u => u.Gym).SingleOrDefaultAsync(u => u.UserId == id &&
            u.UserRoles.Any(r => r.Role.RoleName == role.ToString()), ct);

    private IQueryable<PTAvailability> Templates(string trainerId) => db.PTAvailabilities
        .Where(x => x.PTUserId == trainerId && x.DayOfWeek != null && x.SlotStatus == SlotStatus.Available);

    private IQueryable<PTSession> Occupied(DateTime now) =>
        db.PTSessions.Where(PersonalTrainingRules.OccupiesTime(now));

    private static bool InHours(Gym gym, TimeOnly start, TimeOnly end) =>
        gym.OpenTime is { } open && gym.CloseTime is { } close && start >= open && end <= close && end > start;

    private IQueryable<MemberMembership> Coverage(string memberId) => db.MemberMemberships.Where(x =>
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
            .Select(s => new { s.SessionDate, s.StartTime, s.EndTime, s.MemberUserId }).ToListAsync(ct);
        var yoga = await Yoga(trainerId, memberId, from, through)
            .Select(s => new { s.ScheduledDate, s.StartTime, s.EndTime }).ToListAsync(ct);
        return dates.Where(d => pt.Any(s => s.SessionDate == d.Date &&
            (!allowSecondMemberSession && s.MemberUserId == memberId ||
             PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime))) ||
            yoga.Any(s => s.ScheduledDate == d.Date &&
             PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime))).ToHashSet();
    }

    private IQueryable<YogaClassSchedule> Yoga(string trainerId, string? memberId, DateOnly from, DateOnly through) =>
        db.YogaClassSchedules.Where(s => s.ScheduledDate >= from && s.ScheduledDate <= through &&
            (s.ScheduleStatus == YogaScheduleStatus.Scheduled || s.ScheduleStatus == YogaScheduleStatus.Ongoing) &&
            (s.YogaClass.InstructorUserId == trainerId || memberId != null &&
                s.Bookings.Any(b => b.MemberUserId == memberId && b.BookingStatus == YogaBookingStatus.Confirmed)));

    private void Audit(string? actor, string entity, int id, string value) =>
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor, ActionType = AuditActionType.Update, AuditEntity = entity,
            EntityId = id, NewValue = value, OccurredAt = Now
        });

    private void Notify(string recipient, string title, string message, string entity, int id) =>
        db.Notifications.Add(new Notification
        {
            RecipientUserId = recipient, Title = title, Message = message, NotificationType = "PersonalTraining",
            EntityType = entity, EntityId = id, NotifiedAt = Now
        });

    public async Task ExpirePending(CancellationToken ct)
    {
        var now = Now;
        await Write(async () =>
        {
            var expired = await db.PTPurchases.Include(p => p.Sessions)
                .Include(p => p.Order).ThenInclude(o => o!.PaymentTransaction).Where(p =>
                    p.Status == PTPurchaseStatus.PendingPayment && p.HoldExpiresAt <= now)
                .OrderBy(p => p.Id).Take(100).ToListAsync(ct);
            foreach (var contract in expired)
            {
                contract.Status = PTPurchaseStatus.Expired;
                if (contract.Order is { OrderStatus: OrderStatus.Pending, PaymentTransaction: null } order)
                    order.OrderStatus = OrderStatus.Expired;
                foreach (var session in contract.Sessions.Where(s => s.SessionStatus == PTSessionStatus.CheckoutHold))
                    session.SessionStatus = PTSessionStatus.Cancelled;
            }

            var proposals = await db.PTSessions.Where(s =>
                (s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.MakeUpPending ||
                    s.SessionStatus == PTSessionStatus.RescheduleRequested) &&
                s.ProposalExpiresAt <= now).OrderBy(s => s.Id).Take(100).ToListAsync(ct);
            foreach (var session in proposals)
            {
                var isMakeUp = session.SessionStatus == PTSessionStatus.MakeUpPending;
                if (session.SessionStatus == PTSessionStatus.RescheduleRequested)
                    CloseMemberReschedule(session, null, "RescheduleExpired", "Yêu cầu đổi lịch đã hết hạn. Lịch cũ được giữ nguyên.");
                else
                    ClearProposal(session);
                if (isMakeUp)
                    session.SessionStatus = PTSessionStatus.Cancelled;
            }
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);
    }
}
