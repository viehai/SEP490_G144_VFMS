using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Payments;

namespace VFMS.Infrastructure.Repositories;

public sealed class PersonalTrainingRepository(VfmsDbContext db) : AuditedRepositoryBase(db), IPersonalTrainingRepository
{
    public void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity => Context.Remove((object)entity);

    public async Task<Result<T>> RunSerializableAsync<T>(Func<Task<Result<T>>> work, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            return Result<T>.Failure("Lịch vừa thay đổi. Vui lòng tải lại và thử lại.", "PT_CONFLICT", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
        {
            return Result<T>.Failure("Dữ liệu vừa thay đổi. Vui lòng tải lại và thử lại.", "PT_CONFLICT", 409);
        }
    }

    private static async Task<PTPage<T>> PageAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling((double)total / pageSize)));
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items, total, page);
    }

    // ── Users and trainers ───────────────────────────────────────

    private IQueryable<ApplicationUser> ActiveUsers =>
        Context.ApplicationUsers.Where(u => u.AccountStatus == AccountStatus.Active && !u.IsBanned);

    public Task<ApplicationUser?> GetActiveUserInRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default) =>
        ActiveUsers.Include(u => u.Gym).SingleOrDefaultAsync(u => u.UserId == userId &&
            u.UserRoles.Any(r => r.Role.RoleName == roleName), cancellationToken);

    public async Task<IReadOnlyList<string>> GetGymManagerIdsAsync(string trainerUserId, CancellationToken cancellationToken = default)
    {
        var gymId = await Context.ApplicationUsers.Where(u => u.UserId == trainerUserId)
            .Select(u => (int?)u.GymId).FirstOrDefaultAsync(cancellationToken);
        return await ActiveUsers.Where(u => u.GymId == gymId && u.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.Manager)))
            .Select(u => u.UserId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetManagerNamesAsync(CancellationToken cancellationToken = default) =>
        await Context.ApplicationUsers.Where(u => u.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.Manager)))
            .ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);

    public Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default) =>
        Context.ApplicationUsers.Where(u => u.UserId == userId).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken);

    public Task<int?> GetRoleIdAsync(string roleName, CancellationToken cancellationToken = default) =>
        Context.ApplicationRoles.Where(role => role.RoleName == roleName).Select(role => (int?)role.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ApplicationUserRole?> GetRoleAssignmentAsync(string userId, int roleId, CancellationToken cancellationToken = default) =>
        Context.ApplicationUserRoles.SingleOrDefaultAsync(role => role.UserId == userId && role.RoleId == roleId, cancellationToken);

    private IQueryable<YogaClass> ActiveYogaAssignments => Context.YogaClasses.Where(c =>
        c.ClassStatus == YogaClassStatus.Draft || c.ClassStatus == YogaClassStatus.Published);

    public Task<bool> HasActiveYogaAssignmentsAsync(string userId, CancellationToken cancellationToken = default) =>
        ActiveYogaAssignments.AnyAsync(c => c.InstructorUserId == userId, cancellationToken);

    // Feature 4 serves trainers whose Trainer Role is PT only; Yoga trainers never sell or take over PT contracts.
    private IQueryable<TrainerProfile> PTProfiles => Context.TrainerProfiles
        .Where(p => p.IsActive && p.TrainerRole!.RoleName == TrainerRole.PT);

    public Task<TrainerProfile?> GetPTProfileAsync(string trainerUserId, CancellationToken cancellationToken = default) =>
        PTProfiles.SingleOrDefaultAsync(p => p.PTUserId == trainerUserId, cancellationToken);

    public async Task<IReadOnlyList<TrainerProfile>> GetNotAcceptingProfilesAsync(CancellationToken cancellationToken = default) =>
        await Context.TrainerProfiles.Where(p => p.IsActive && !p.AcceptingNewMembers && p.NotAcceptingSince != null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PTReplacementTrainer>> GetReplacementTrainersAsync(int gymId, TrainerLevel? level,
        string excludingTrainerId, CancellationToken cancellationToken = default)
    {
        var yoga = ActiveYogaAssignments.Select(c => c.InstructorUserId);
        return await (from profile in PTProfiles
                      join user in ActiveUsers on profile.PTUserId equals user.UserId
                      where profile.AcceptingNewMembers &&
                          profile.Level == level && profile.PTUserId != excludingTrainerId &&
                          user.GymId == gymId && !yoga.Contains(profile.PTUserId) &&
                          user.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.PT))
                      orderby user.FullName
                      select new PTReplacementTrainer(user.UserId, profile.Id, user.FullName, profile.Level.ToString(),
                          profile.Specialty)).ToListAsync(cancellationToken);
    }

    // ── Schedule ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<PTAvailability>> GetPublishedSlotsAsync(string trainerUserId, CancellationToken cancellationToken = default) =>
        await Context.PTAvailabilities.Where(x => x.PTUserId == trainerUserId && x.DayOfWeek != null &&
            x.SlotStatus == SlotStatus.Available).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PTSession>> GetOccupyingSessionsAsync(DateTime now, DateOnly from, DateOnly through,
        string? trainerUserId, string? memberUserId, int? ignoreSessionId = null, int? ignorePurchaseId = null,
        CancellationToken cancellationToken = default) =>
        await Context.PTSessions.AsNoTracking().Where(PersonalTrainingRules.OccupiesTime(now))
            .Where(s => s.SessionDate >= from && s.SessionDate <= through &&
                ((trainerUserId != null && s.PTUserId == trainerUserId) || (memberUserId != null && s.MemberUserId == memberUserId)) &&
                (ignoreSessionId == null || s.Id != ignoreSessionId) &&
                (ignorePurchaseId == null || s.PTPurchaseId != ignorePurchaseId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<YogaClassSchedule>> GetYogaSessionsAsync(string? instructorUserId, string? memberUserId,
        DateOnly from, DateOnly through, CancellationToken cancellationToken = default) =>
        await Context.YogaClassSchedules.AsNoTracking().Where(s => s.ScheduledDate >= from && s.ScheduledDate <= through &&
            (s.ScheduleStatus == YogaScheduleStatus.Scheduled || s.ScheduleStatus == YogaScheduleStatus.Ongoing) &&
            ((instructorUserId != null && s.YogaClass.InstructorUserId == instructorUserId) || memberUserId != null &&
                s.YogaClass.Bookings.Any(b => b.MemberUserId == memberUserId && b.BookingStatus == YogaBookingStatus.Confirmed &&
                    (b.StartDate == null || b.StartDate <= s.ScheduledDate) &&
                    (b.EndDate == null || b.EndDate >= s.ScheduledDate))))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PTSession>> GetTeachingSessionsAsync(string trainerUserId, DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default) =>
        await Context.PTSessions.AsNoTracking().Include(s => s.Member)
            .Where(s => s.PTUserId == trainerUserId && s.SessionDate >= from && s.SessionDate <= through &&
                s.SessionStatus != PTSessionStatus.CheckoutHold && s.SessionStatus != PTSessionStatus.Cancelled &&
                s.SessionStatus != PTSessionStatus.MakeUpPending && s.SessionStatus != PTSessionStatus.MakeUpRequested &&
                s.SessionStatus != PTSessionStatus.FreezeHold)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).ToListAsync(cancellationToken);

    // ── Packages (UC 4.3) ────────────────────────────────────────

    private IQueryable<Package> PTPackages => Context.Packages.Include(p => p.Components)
        .Where(p => p.Components.Count == 1 &&
            p.Components.Any(c => c.ComponentType == ComponentType.PT && c.ApplicableTrainerLevel != null));

    public async Task<IReadOnlyList<Package>> GetPTPackagesAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
        await PTPackages.AsNoTracking().Where(p => !activeOnly || p.PackageStatus == PackageStatus.Active)
            .OrderBy(p => p.Id).ToListAsync(cancellationToken);

    public Task<Package?> GetPTPackageAsync(int id, bool activeOnly, CancellationToken cancellationToken = default) =>
        PTPackages.SingleOrDefaultAsync(p => p.Id == id && (!activeOnly || p.PackageStatus == PackageStatus.Active), cancellationToken);

    public async Task<IReadOnlyDictionary<int, int>> CountRunningContractsByPackageAsync(DateOnly today,
        CancellationToken cancellationToken = default) =>
        await (from purchase in Context.PTPurchases
               join item in Context.OrderItems on purchase.OrderId equals (int?)item.OrderId
               where !purchase.IsCompleted && purchase.EndDate >= today &&
                   (purchase.Status == PTPurchaseStatus.Active || purchase.Status == PTPurchaseStatus.Frozen)
               select new { item.PackageId, PurchaseId = purchase.Id })
            .Distinct().GroupBy(x => x.PackageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);

    public Task<bool> PackageHasHistoryAsync(int packageId, CancellationToken cancellationToken = default) =>
        Context.Packages.IgnoreQueryFilters().AnyAsync(p => p.Id == packageId &&
            (p.OrderItems.Any() || p.Components.Any(c => c.MemberMemberships.Any())), cancellationToken);

    // ── Contracts ────────────────────────────────────────────────

    private IQueryable<PTPurchase> Contracts => Context.PTPurchases
        .Include(p => p.Sessions).Include(p => p.PTFreezes).Include(p => p.PTUser).ThenInclude(u => u.Gym).Include(p => p.Member)
        .AsSplitQuery();

    public Task<PTPurchase?> GetContractAsync(int id, CancellationToken cancellationToken = default) =>
        Contracts.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PTPurchase?> GetContractForSessionAsync(int sessionId, string userId, bool asTrainer,
        CancellationToken cancellationToken = default) =>
        Contracts.SingleOrDefaultAsync(p => (asTrainer ? p.PTUserId == userId : p.MemberUserId == userId) &&
            p.Sessions.Any(s => s.Id == sessionId), cancellationToken);

    public Task<PTPurchase?> GetContractForFreezeAsync(int freezeId, CancellationToken cancellationToken = default) =>
        Contracts.SingleOrDefaultAsync(p => p.PTFreezes.Any(f => f.Id == freezeId), cancellationToken);

    public Task<string?> GetContractTrainerIdAsync(int id, string memberId, CancellationToken cancellationToken = default) =>
        Context.PTPurchases.Where(x => x.Id == id && x.MemberUserId == memberId).Select(x => x.PTUserId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PTPurchase?> GetContractForOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        Context.PTPurchases.Include(p => p.Sessions).SingleOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public Task<PTPurchase?> GetMemberContractByOrderNumberAsync(string memberId, string orderNumber,
        CancellationToken cancellationToken = default) =>
        Context.PTPurchases.Include(p => p.Order!).ThenInclude(o => o.PaymentTransaction!).ThenInclude(t => t.Invoice)
            .SingleOrDefaultAsync(p => p.MemberUserId == memberId && p.Order!.OrderNumber == orderNumber, cancellationToken);

    // UC 4.5: an unpaid checkout is listed only while its hold is live; once it expires it is not a contract.
    private IQueryable<PTPurchase> VisibleContracts(string userId, DateTime now) =>
        Contracts.AsNoTracking().Include(p => p.Order)
            .Where(p => p.OrderId == null || p.Order!.OrderStatus == OrderStatus.Paid ||
                p.MemberUserId == userId && p.Status == PTPurchaseStatus.PendingPayment && p.HoldExpiresAt > now)
            .Where(p => p.MemberUserId == userId || p.PTUserId == userId && p.Status != PTPurchaseStatus.PendingPayment);

    public async Task<IReadOnlyList<PTPurchase>> GetVisibleContractsAsync(string userId, DateTime now, int? id,
        CancellationToken cancellationToken = default) =>
        await VisibleContracts(userId, now).Where(p => id == null || p.Id == id)
            .OrderByDescending(p => p.Id).Take(100).ToListAsync(cancellationToken);

    public Task<PTPage<PTPurchase>> GetMemberContractPageAsync(string memberId, DateTime now, string? trainerId, bool? completed,
        int page, int pageSize, CancellationToken cancellationToken = default) =>
        PageAsync(VisibleContracts(memberId, now).Where(p => p.MemberUserId == memberId &&
                (trainerId == null || p.PTUserId == trainerId) &&
                (completed == null || (p.IsCompleted || p.Status == PTPurchaseStatus.Completed) == completed))
            .OrderByDescending(p => p.Id), page, pageSize, cancellationToken);

    public Task<PTPage<PTPurchase>> GetAssignedContractPageAsync(string trainerId, DateTime now, DateOnly today, string? search,
        bool historical, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // UC 4.6 / GBR-14: a contract stays 'Currently Assigned' until its end date, and through the 14-day
        // make-up period only while it still has make-up to use.
        var running = PersonalTrainingRules.StillRunning(today);
        var query = VisibleContracts(trainerId, now).Where(p => p.PTUserId == trainerId &&
            (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen ||
             p.Status == PTPurchaseStatus.Completed || p.Status == PTPurchaseStatus.Expired));
        query = query.Where(historical ? PersonalTrainingRules.Not(running) : running);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Member.FullName.Contains(search));
        return PageAsync(query.OrderBy(p => p.Member.FullName).ThenByDescending(p => p.Id), page, pageSize, cancellationToken);
    }

    public Task<bool> HasRunningContractAsync(string trainerUserId, DateOnly today, CancellationToken cancellationToken = default) =>
        Context.PTPurchases.Where(p => p.PTUserId == trainerUserId).Where(PersonalTrainingRules.StillRunning(today))
            .AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<PTPurchase>> GetExpiredCheckoutsAsync(DateTime now, int take, CancellationToken cancellationToken = default) =>
        await Context.PTPurchases.Include(p => p.Sessions)
            .Include(p => p.Order!).ThenInclude(o => o.PaymentTransaction)
            .Include(p => p.Order!).ThenInclude(o => o.VoucherRedemption)
            .AsSplitQuery()
            .Where(p => p.Status == PTPurchaseStatus.PendingPayment && p.HoldExpiresAt <= now)
            .OrderBy(p => p.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<int>> GetReviewedContractIdsAsync(string memberId, CancellationToken cancellationToken = default) =>
        await Context.TrainerReviews.Where(r => r.MemberUserId == memberId && r.PTPurchaseId != null)
            .Select(r => r.PTPurchaseId!.Value).ToListAsync(cancellationToken);

    public Task<bool> HasReviewAsync(int contractId, CancellationToken cancellationToken = default) =>
        Context.TrainerReviews.AnyAsync(r => r.PTPurchaseId == contractId, cancellationToken);

    // ── Sessions ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<PTSession>> GetExpiredProposalsAsync(DateTime now, int take, CancellationToken cancellationToken = default) =>
        await Context.PTSessions.Where(s =>
                (s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.MakeUpPending ||
                    s.SessionStatus == PTSessionStatus.MakeUpRequested || s.SessionStatus == PTSessionStatus.RescheduleRequested) &&
                s.ProposalExpiresAt <= now)
            .OrderBy(s => s.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PTSession>> GetUnrecordedPastSessionsAsync(DateOnly today, int take,
        CancellationToken cancellationToken = default) =>
        await Context.PTSessions.Include(s => s.PTPurchase).ThenInclude(p => p.Sessions)
            .Where(s => s.SessionDate < today && (s.SessionStatus == PTSessionStatus.Scheduled ||
                s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.RescheduleRequested))
            .OrderBy(s => s.Id).Take(take).ToListAsync(cancellationToken);

    public Task<PTSession?> GetSessionWithContractAsync(int id, CancellationToken cancellationToken = default) =>
        Context.PTSessions.Include(s => s.PTPurchase).ThenInclude(p => p.Sessions)
            .Include(s => s.PTPurchase).ThenInclude(p => p.PTFreezes)
            .AsSplitQuery().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<PTSession?> GetReportedSessionAsync(int id, CancellationToken cancellationToken = default) =>
        Context.PTSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.TrainerAbsenceReportedAt != null, cancellationToken);

    public Task<PTSession?> GetTrainerSessionAsync(int id, string trainerUserId, CancellationToken cancellationToken = default) =>
        Context.PTSessions.SingleOrDefaultAsync(s => s.Id == id && s.PTUserId == trainerUserId, cancellationToken);

    public Task<PTPage<PTSession>> GetDisputePageAsync(bool processed, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        PageAsync(Context.PTSessions.AsNoTracking().Include(s => s.Member).Include(s => s.PTUser).AsSplitQuery()
            .Where(s => s.TrainerAbsenceReportedAt != null && (processed
                ? s.TrainerAbsenceReviewedAt != null
                : s.SessionStatus == PTSessionStatus.Disputed))
            .OrderByDescending(s => s.TrainerAbsenceReportedAt), page, pageSize, cancellationToken);

    public async Task<IReadOnlyList<CheckIn>> GetCheckInsAsync(string memberId, DateTime from, DateTime to,
        CancellationToken cancellationToken = default) =>
        await Context.CheckIns.AsNoTracking().Include(c => c.HandledByUser).Include(c => c.CheckedOutByUser).AsSplitQuery()
            .Where(c => c.MemberUserId == memberId && c.CheckinTime >= from && c.CheckinTime < to)
            .OrderBy(c => c.CheckinTime).ToListAsync(cancellationToken);

    public Task<string?> GetFreezeHoldTrainerIdAsync(int freezeId, string memberId, CancellationToken cancellationToken = default) =>
        Context.PTSessions.Where(s => s.PTFreezeId == freezeId && s.MemberUserId == memberId &&
            s.SessionStatus == PTSessionStatus.FreezeHold).Select(s => s.PTUserId).FirstOrDefaultAsync(cancellationToken);

    // ── Freezes (UC 4.8, UC 4.9) ─────────────────────────────────

    public async Task<PTPage<PTFreezeRow>> GetFreezePageAsync(int? gymId, string? memberId, int? contractId, bool processed,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.PTFreezes.AsNoTracking().Where(f => gymId != null
            ? f.PTPurchase.PTUser.GymId == gymId &&
                (f.RequestStatus != PTFreezeRequestStatus.Pending && f.ChangeRequestStatus != PTFreezeChangeStatuses.Pending) == processed
            : f.PTPurchase.MemberUserId == memberId && f.PTPurchaseId == contractId);
        var rows = await PageAsync(query.OrderByDescending(f => f.Id).Select(f => new
        {
            Freeze = f,
            Contract = f.PTPurchase,
            MemberName = f.PTPurchase.Member.FullName,
            TrainerName = f.PTPurchase.PTUser.FullName,
            ReplacementName = Context.ApplicationUsers.Where(u => u.UserId == f.ReplacementTrainerId)
                .Select(u => u.FullName).FirstOrDefault(),
            Held = f.PTPurchase.Sessions.Where(s => s.PTFreezeId == f.Id && s.SessionStatus == PTSessionStatus.FreezeHold)
                .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime)
                .Select(s => new { s.SessionDate, s.StartTime, s.EndTime }).ToList()
        }), page, pageSize, cancellationToken);
        return new(rows.Items.Select(row => new PTFreezeRow(row.Freeze, row.Contract, row.MemberName, row.TrainerName,
            row.ReplacementName, row.Held.Select(h => new PTCalendarSlot(0, h.SessionDate, h.StartTime, h.EndTime, "Held")).ToArray()))
            .ToList(), rows.TotalCount, rows.Page);
    }

    public async Task<IReadOnlyList<PTFreeze>> GetFreezesDueForReminderAsync(DateTime now, int take,
        CancellationToken cancellationToken = default) =>
        await Context.PTFreezes.Include(f => f.PTPurchase).ThenInclude(p => p.PTUser).Where(f =>
                f.RequestStatus == PTFreezeRequestStatus.Pending && f.CreatedAt <= now.AddHours(-24) &&
                (f.ManagerRemindedAt == null || f.CreatedAt <= now.AddHours(-48) && f.ManagerRemindedAt <= now.AddHours(-24)))
            .OrderBy(f => f.Id).Take(take).ToListAsync(cancellationToken);

    // ── Payments and vouchers (UC 4.4) ───────────────────────────

    public async Task<IReadOnlyList<PaymentTransaction>> GetRefundsAsync(int paymentId, CancellationToken cancellationToken = default) =>
        await Context.PaymentTransactions.AsNoTracking()
            .Where(payment => (payment.Id == paymentId || payment.SourcePaymentTransactionId == paymentId) &&
                payment.RefundStatus != null && payment.RefundAmount > 0 && payment.PaidAt != null &&
                payment.GatewayTransactionReference != null)
            .OrderByDescending(payment => payment.Id).ToListAsync(cancellationToken);

    public Task<VoucherRedemption?> GetRedemptionForOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        Context.VoucherRedemptions.Include(r => r.Voucher).SingleOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public Task<bool> IsTransferRecordedAsync(PaymentTransaction payment, string reference, CancellationToken cancellationToken = default) =>
        CheckoutRefunds.IsRecorded(Context, payment, reference, cancellationToken);

    public void RecordExtraTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now) =>
        CheckoutRefunds.RecordExtraTransfer(Context, payment, transfer, now);

    public void RecordUnappliedTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now) =>
        CheckoutRefunds.RecordUnappliedTransfer(Context, payment, transfer, now, notify: false);
}
