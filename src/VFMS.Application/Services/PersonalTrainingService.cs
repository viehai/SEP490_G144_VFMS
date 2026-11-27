using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService(IPersonalTrainingRepository repo, TimeProvider clock,
    IMembershipPaymentGateway gateway, IMembershipPurchaseRepository payments, IBannedWordService bannedWords)
    : IPersonalTrainingService, IPTPaymentFulfillment
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now.AddHours(7));
    private static DateTime Utc(DateOnly date, TimeOnly time) =>
        date.ToDateTime(time, DateTimeKind.Utc).AddHours(-7);

    private Task<Result<T>> Write<T>(Func<Task<Result<T>>> work, CancellationToken ct) => repo.RunSerializableAsync(work, ct);

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, "PT_VALIDATION");
    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, "PT_CONFLICT", 409);
    private static Result<T> Missing<T>() => Result<T>.Failure("Không tìm thấy dữ liệu PT.", "PT_NOT_FOUND", 404);
    private static Result<T> Forbidden<T>() => Result<T>.Failure("Tài khoản không có quyền thực hiện.", "PT_FORBIDDEN", 403);

    private Task<ApplicationUser?> User(string id, UserRole role, CancellationToken ct) =>
        repo.GetActiveUserInRoleAsync(id, role.ToString(), ct);

    private Task<IReadOnlyList<PTAvailability>> Templates(string trainerId, CancellationToken ct) =>
        repo.GetPublishedSlotsAsync(trainerId, ct);

    private async Task<PTPurchase?> MemberContract(string memberId, int id, CancellationToken ct) =>
        await repo.GetContractAsync(id, ct) is { } contract && contract.MemberUserId == memberId ? contract : null;

    private const int PageSize = 10;

    private static Result<PagedResponse<TResponse>> PageOf<T, TResponse>(PTPage<T> page, Func<T, TResponse> map) =>
        Result<PagedResponse<TResponse>>.Success(new(page.Page, PageSize, page.TotalCount, page.Items.Select(map).ToArray()));

    private static bool InHours(Gym gym, TimeOnly start, TimeOnly end) =>
        gym.OpenTime is { } open && gym.CloseTime is { } close && start >= open && end <= close && end > start;

    private static bool IsWholeHour(TimeOnly time) => time.Ticks % TimeSpan.TicksPerHour == 0;

    private async Task<bool> HasConflict(string trainerId, string memberId,
        IReadOnlyList<PTCalendarSlot> dates, DateTime now, CancellationToken ct, int? ignoreSession = null,
        int? ignorePurchase = null) =>
        (await ConflictingSlots(trainerId, memberId, dates, now, ct, ignoreSession, ignorePurchase)).Count != 0;

    private async Task<HashSet<PTCalendarSlot>> ConflictingSlots(string trainerId, string memberId,
        IReadOnlyList<PTCalendarSlot> dates, DateTime now, CancellationToken ct, int? ignoreSession = null,
        int? ignorePurchase = null)
    {
        if (dates.Count == 0)
            return [];
        var from = dates.Min(s => s.Date);
        var through = dates.Max(s => s.Date);
        var pt = await repo.GetOccupyingSessionsAsync(now, from, through, trainerId, memberId, ignoreSession, ignorePurchase, ct);
        var yoga = await repo.GetYogaSessionsAsync(trainerId, memberId, from, through, ct);
        // A Member may train several PT sessions a day, never two at the same time.
        return dates.Where(d => pt.Any(s => s.SessionDate == d.Date &&
             PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime)) ||
            yoga.Any(s => s.ScheduledDate == d.Date &&
             PersonalTrainingRules.Overlaps(d.StartTime, d.EndTime, s.StartTime, s.EndTime))).ToHashSet();
    }

    private void Audit(string? actor, string entity, int id, string value) =>
        repo.Add(new AuditLog
        {
            ActorUserId = actor,
            ActionType = AuditActionType.Update,
            AuditEntity = entity,
            EntityId = id,
            NewValue = value,
            OccurredAt = Now
        });

    private void Notify(string recipient, string title, string message, string entity, int id) =>
        repo.Add(new Notification
        {
            RecipientUserId = recipient,
            Title = title,
            Message = message,
            NotificationType = "PersonalTraining",
            EntityType = entity,
            EntityId = id,
            NotifiedAt = Now
        });

    /// <summary>GBR-104: removes the PT role after more than 60 accumulated 'Not Accepting' days, once no contract is still running,
    /// including its 14-day make-up period (GBR-14).</summary>
    public async Task ExpireInactiveTrainers(DateOnly today, CancellationToken ct)
    {
        var profiles = await repo.GetNotAcceptingProfilesAsync(ct);
        var ptRoleId = await repo.GetRoleIdAsync(nameof(UserRole.PT), ct);
        foreach (var profile in profiles)
        {
            // Running contracts pause accrual; a brief return to accepting preserves already accumulated days.
            // shortcut: contract pauses are observed by the daily sweep, as in the previous inactivity policy.
            var running = await repo.HasRunningContractAsync(profile.PTUserId, today, ct);
            TrainerReadinessRules.AccumulateInactiveDays(profile, today, running);
            if (running)
                continue;
            if (!TrainerReadinessRules.HasExpired(profile))
                continue;
            profile.IsActive = false;
            if (ptRoleId is { } roleId)
            {
                var assignment = await repo.GetRoleAssignmentAsync(profile.PTUserId, roleId, ct);
                if (assignment != null) repo.Remove(assignment);
            }
            Audit(profile.PTUserId, nameof(TrainerProfile), profile.Id, "PTInactivityExpired");
            Notify(profile.PTUserId, "Quyền PT đã kết thúc",
                "Bạn đã tích lũy quá 60 ngày không nhận học viên mới và không còn hợp đồng đang chạy.",
                nameof(TrainerProfile), profile.Id);
        }
        await repo.SaveChangesAsync(ct);
    }

    public async Task ExpirePending(CancellationToken ct)
    {
        var now = Now;
        await Write(async () =>
        {
            var expired = await repo.GetExpiredCheckoutsAsync(now, 100, ct);
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

            var proposals = await repo.GetExpiredProposalsAsync(now, 100, ct);
            foreach (var session in proposals)
            {
                var isMakeUp = session.SessionStatus is PTSessionStatus.MakeUpPending or PTSessionStatus.MakeUpRequested;
                if (PersonalTrainingRules.PublicSessionStatus(session.SessionStatus) == PTSessionStatus.RescheduleRequested)
                    CloseMemberReschedule(session, null, "RescheduleExpired", "Yêu cầu đổi lịch đã hết hạn. Lịch cũ được giữ nguyên.");
                else
                    ClearProposal(session);
                if (isMakeUp)
                    session.SessionStatus = PTSessionStatus.Cancelled;
            }
            await repo.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);
    }
}
