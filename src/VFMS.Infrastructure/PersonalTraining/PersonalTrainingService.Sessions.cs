using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.PersonalTraining;

public sealed partial class PersonalTrainingService
{
    private IQueryable<PTPurchase> ContractsWithSessions => db.PTPurchases.Include(p => p.Sessions).Include(p => p.PTFreezes)
        .Include(p => p.PTUser).ThenInclude(t => t.Gym).AsSplitQuery();

    private PTPurchaseStatus EffectiveStatus(PTPurchase p)
    {
        if (p.IsCompleted || p.Status == PTPurchaseStatus.Completed)
            return PTPurchaseStatus.Completed;
        if (p.Status == PTPurchaseStatus.PendingPayment)
            return p.HoldExpiresAt <= Now ? PTPurchaseStatus.Expired : p.Status;
        if (p.Status == PTPurchaseStatus.Expired)
            return p.Status;
        if (p.PTFreezes.Any(f => Approved(f) && f.ActualStartDate <= Today && Today < f.ActualEndDate))
            return PTPurchaseStatus.Frozen;
        return Today > p.EndDate ? PTPurchaseStatus.Expired : PTPurchaseStatus.Active;
    }

    private static PTSessionResponse SessionResponse(PTSession s, DateTime now)
    {
        var awaitingChange = s.SessionStatus is PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested;
        var proposed = awaitingChange && s.ProposalExpiresAt > now;
        if (s.SessionStatus == PTSessionStatus.MakeUpPending)
        {
            var pending = s.ProposalExpiresAt is { } deadline && deadline > now;
            return new(s.Id, s.SessionDate, s.StartTime, s.EndTime,
                pending ? nameof(PTSessionStatus.MakeUpPending) : nameof(PTSessionStatus.Cancelled),
                s.CreditUnits, s.Note, s.NoteUpdatedAt, pending
                    ? new(s.SessionDate, s.StartTime, s.EndTime, "MakeUp",
                        DateTime.SpecifyKind(s.ProposalExpiresAt!.Value, DateTimeKind.Utc)) : null);
        }
        return new(s.Id, s.SessionDate, s.StartTime, s.EndTime,
            (awaitingChange && !proposed ? PTSessionStatus.Scheduled : s.SessionStatus).ToString(),
            s.CreditUnits, s.Note, s.NoteUpdatedAt, proposed
                ? new(s.ProposedDate!.Value, s.ProposedStartTime!.Value, s.ProposedEndTime!.Value,
                    s.ProposedAdjustmentType!.Value.ToString(),
                    DateTime.SpecifyKind(s.ProposalExpiresAt!.Value, DateTimeKind.Utc)) : null);
    }

    private PTContractResponse ContractResponse(PTPurchase p)
    {
        var status = EffectiveStatus(p);
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        if (status == PTPurchaseStatus.PendingPayment)
            reserved = p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.CheckoutHold).Sum(s => s.CreditUnits);
        return new(p.Id, p.PTUserId, p.MemberUserId, p.PackageNameSnapshot, status.ToString(), p.StartDate, p.EndDate,
            p.TotalSessions, used, reserved, Math.Max(0, p.TotalSessions - used - reserved), Math.Max(0, p.TotalSessions - used),
            p.FreezeMinimumDaysSnapshot, p.FreezeMaxTotalDaysSnapshot, RemainingFreezeDays(p),
            p.Sessions.OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Select(s => SessionResponse(s, Now)).ToArray());
    }

    public async Task<Result<IReadOnlyList<PTContractResponse>>> MyContracts(string actor, int? id, CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null && await User(actor, UserRole.PT, ct) is null)
            return Forbidden<IReadOnlyList<PTContractResponse>>();
        var contracts = await ContractsWithSessions.AsNoTracking()
            .Where(p => (p.MemberUserId == actor || p.PTUserId == actor && p.Status != PTPurchaseStatus.PendingPayment &&
                p.Status != PTPurchaseStatus.Expired) && (id == null || p.Id == id))
            .OrderByDescending(p => p.Id).Take(100).ToListAsync(ct);
        return id != null && contracts.Count == 0 ? Missing<IReadOnlyList<PTContractResponse>>() :
            Result<IReadOnlyList<PTContractResponse>>.Success(contracts.Select(ContractResponse).ToArray());
    }

    public async Task<Result<IReadOnlyList<PTAssignedMember>>> Assigned(string actor, string? search, CancellationToken ct)
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<IReadOnlyList<PTAssignedMember>>();
        var query = ContractsWithSessions.AsNoTracking().Include(p => p.Member)
            .Where(p => p.PTUserId == actor && p.EndDate >= Today &&
                (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen) && !p.IsCompleted);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Member.FullName.Contains(search));
        var data = await query.OrderBy(p => p.Member.FullName).ThenBy(p => p.Id).Take(100).ToListAsync(ct);
        return Result<IReadOnlyList<PTAssignedMember>>.Success(data.Select(p => new PTAssignedMember(p.Id, p.MemberUserId,
            p.Member.FullName, p.Member.AvatarUrl, p.Member.Email, p.Member.PhoneNumber, p.TotalSessions,
            p.TotalSessions - PersonalTrainingRules.Credits(p.Sessions).Used, p.EndDate,
            p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.Completed).Max(s => (DateOnly?)s.SessionDate))).ToArray());
    }

    private Task<PTPurchase?> ContractForSession(string actor, int id, bool trainer, CancellationToken ct) =>
        ContractsWithSessions.SingleOrDefaultAsync(p => (trainer ? p.PTUserId == actor : p.MemberUserId == actor) &&
            p.Sessions.Any(s => s.Id == id), ct);

    private static void ClearProposal(PTSession s)
    {
        s.ProposedDate = null;
        s.ProposedStartTime = null;
        s.ProposedEndTime = null;
        s.ProposedAdjustmentType = null;
        s.ProposalExpiresAt = null;
        s.ProposalEmailSentAt = null;
        s.ProposalEmailRetryAt = null;
        s.SessionStatus = PTSessionStatus.Scheduled;
    }

    private async Task<bool> CanAdjust(PTPurchase p, PTSession s, DateOnly date, TimeOnly start, TimeOnly end,
        PTAdjustmentType type, DateTime now, CancellationToken ct, bool isMakeUp = false)
    {
        var units = type == PTAdjustmentType.DoubleSession ? 2 : s.CreditUnits;
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        if (EffectiveStatus(p) != PTPurchaseStatus.Active || date < p.StartDate || date > p.EndDate ||
            Utc(date, start) <= now || !InHours(p.PTUser.Gym, start, end) ||
            p.TotalSessions - used - reserved < (isMakeUp ? units : units - s.CreditUnits) ||
            p.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
                f.RequestedStartDate <= date && date < (f.ActualEndDate ?? f.RequestedEndDate)) ||
            !await Coverage(p.MemberUserId).AnyAsync(m => m.StartDate <= Today && m.EndDate >= date, ct))
            return false;
        return !await HasConflict(p.PTUserId, p.MemberUserId, [new(0, date, start, end, "Available")],
            now, ct, ignoreSession: s.Id, allowSecondMemberSession: true);
    }

    public Task<Result<PTSessionResponse>> ProposeMakeUp(string actor, int id, PTMakeUpRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.PT, ct) is null)
                return Forbidden<PTSessionResponse>();
            var contract = await ContractForSession(actor, id, true, ct);
            if (contract is null)
                return Missing<PTSessionResponse>();

            var original = contract.Sessions.Single(s => s.Id == id);
            var now = Now;
            if (original.SessionStatus != PTSessionStatus.Cancelled || original.CancelledAt == null ||
                original.IsCreditForfeited ||
                contract.Sessions.Any(s => s.ReplacementForSessionId == id &&
                    (s.SessionStatus == PTSessionStatus.MakeUpPending
                        ? s.ProposalExpiresAt > now
                        : s.SessionStatus != PTSessionStatus.Cancelled)))
                return Conflict<PTSessionResponse>("Buổi này không có credit được trả hoặc đã có buổi thay thế.");

            var session = new PTSession
            {
                MemberUserId = contract.MemberUserId,
                PTUserId = actor,
                SessionDate = request.Date,
                StartTime = request.StartTime,
                EndTime = request.StartTime.AddHours(1),
                CreditUnits = 1,
                SessionStatus = PTSessionStatus.MakeUpPending,
                ReplacementForSessionId = id
            };
            if (!await CanAdjust(contract, session, session.SessionDate, session.StartTime, session.EndTime,
                PTAdjustmentType.Reschedule, now, ct, isMakeUp: true))
                return Conflict<PTSessionResponse>("Lịch hoặc credit không đủ điều kiện.");

            session.ProposalExpiresAt = new[] { now.AddHours(24), Utc(session.SessionDate, session.StartTime) }.Min();
            contract.Sessions.Add(session);
            await db.SaveChangesAsync(ct);
            Audit(actor, nameof(PTSession), session.Id, "MakeUpProposed");
            Notify(session.MemberUserId, "Đề xuất buổi bù PT",
                $"Buổi bù đề xuất {session.SessionDate:dd/MM/yyyy} {session.StartTime:HH:mm}–{session.EndTime:HH:mm}.",
                nameof(PTSession), session.Id);
            await db.SaveChangesAsync(ct);
            return Result<PTSessionResponse>.Success(SessionResponse(session, now), 201);
        }, ct);

    public Task<Result<PTSessionResponse>> Propose(string actor, int id, PTProposalRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, true, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        var now = Now;
        var oldStart = Utc(s.SessionDate, s.StartTime);
        if (s.SessionStatus == PTSessionStatus.ReschedulePending && s.ProposalExpiresAt <= now)
            ClearProposal(s);
        if (s.SessionStatus != PTSessionStatus.Scheduled || oldStart <= now)
            return Conflict<PTSessionResponse>("Chỉ có thể đề xuất cho buổi sắp tới chưa có đề xuất chờ.");
        const PTAdjustmentType type = PTAdjustmentType.DoubleSession;
        if (!string.Equals(request.Type, nameof(PTAdjustmentType.DoubleSession), StringComparison.OrdinalIgnoreCase))
            return Invalid<PTSessionResponse>("PT chỉ có thể đề xuất ca đôi qua chức năng này.");
        if (s.CreditUnits != 1 || request.Date != s.SessionDate || request.StartTime != s.StartTime)
            return Invalid<PTSessionResponse>("Ca đôi kéo dài buổi 60 phút hiện tại.");
        var end = request.StartTime.AddHours(2);
        if (!await CanAdjust(p, s, request.Date, request.StartTime, end, type, now, ct))
            return Conflict<PTSessionResponse>("Lịch hoặc credit không đủ điều kiện.");
        s.ProposedDate = request.Date;
        s.ProposedStartTime = request.StartTime;
        s.ProposedEndTime = end;
        s.ProposedAdjustmentType = type;
        s.ProposalExpiresAt = new[] { now.AddHours(24), oldStart, Utc(request.Date, request.StartTime) }.Min();
        s.SessionStatus = PTSessionStatus.ReschedulePending;
        Audit(actor, nameof(PTSession), s.Id, $"Proposed:{type}:{request.Date:yyyy-MM-dd}:{request.StartTime:HH:mm}");
        Notify(s.MemberUserId, "Đề xuất đổi lịch PT",
            $"Lịch đề xuất {request.Date:dd/MM/yyyy} {request.StartTime:HH:mm}–{end:HH:mm}.",
            nameof(PTSession), s.Id);
        await db.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(s, now));
    }, ct);

    public Task<Result<PTSessionResponse>> DecideProposal(string actor, int id, PTProposalDecision request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, false, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        if (s.SessionStatus == PTSessionStatus.MakeUpPending)
            return await DecideMakeUp(p, s, actor, request, ct);
        if (s.SessionStatus != PTSessionStatus.ReschedulePending || s.ProposalExpiresAt != request.ProposalExpiresAt)
            return Conflict<PTSessionResponse>("Đề xuất đã thay đổi. Vui lòng tải lại.");
        var now = Now;
        var valid = s.ProposalExpiresAt > now;
        if (request.Accept && valid)
        {
            var type = s.ProposedAdjustmentType!.Value;
            valid = await CanAdjust(p, s, s.ProposedDate!.Value, s.ProposedStartTime!.Value,
                s.ProposedEndTime!.Value, type, now, ct);
            if (valid)
            {
                s.SessionDate = s.ProposedDate.Value;
                s.StartTime = s.ProposedStartTime.Value;
                s.EndTime = s.ProposedEndTime.Value;
                if (type == PTAdjustmentType.DoubleSession)
                    s.CreditUnits = 2;
                else
                    s.PTAvailabilityId = await Templates(s.PTUserId).Where(x =>
                            x.DayOfWeek == s.SessionDate.DayOfWeek && x.StartTime == s.StartTime)
                        .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            }
        }
        ClearProposal(s);
        var outcome = !valid ? "ProposalUnavailable" : request.Accept ? "ProposalAccepted" : "ProposalDeclined";
        Audit(actor, nameof(PTSession), s.Id, outcome);
        var message = !valid ? "Lịch đề xuất không còn khả dụng. Lịch cũ được giữ nguyên." :
            request.Accept ? "Hội viên đã xác nhận đổi lịch PT." : "Hội viên đã từ chối đổi lịch PT.";
        Notify(s.PTUserId, "Đề xuất lịch PT", message, nameof(PTSession), s.Id);
        Notify(s.MemberUserId, "Đề xuất lịch PT", message, nameof(PTSession), s.Id);
        await db.SaveChangesAsync(ct);
        return valid ? Result<PTSessionResponse>.Success(SessionResponse(s, now)) : Conflict<PTSessionResponse>(message);
    }, ct);

    private async Task<Result<PTSessionResponse>> DecideMakeUp(PTPurchase contract, PTSession session, string actor,
        PTProposalDecision request, CancellationToken ct)
    {
        if (session.ProposalExpiresAt != request.ProposalExpiresAt)
            return Conflict<PTSessionResponse>("Đề xuất đã thay đổi. Vui lòng tải lại.");
        var now = Now;
        var valid = session.ProposalExpiresAt > now;
        if (valid && request.Accept)
            valid = await CanAdjust(contract, session, session.SessionDate, session.StartTime, session.EndTime,
                PTAdjustmentType.Reschedule, now, ct, isMakeUp: true);

        ClearProposal(session);
        if (!valid || !request.Accept)
            session.SessionStatus = PTSessionStatus.Cancelled;

        var message = !valid ? "Buổi bù không còn khả dụng." :
            request.Accept ? "Hội viên đã xác nhận buổi bù PT." : "Hội viên đã từ chối buổi bù PT.";
        Audit(actor, nameof(PTSession), session.Id,
            !valid ? "MakeUpUnavailable" : request.Accept ? "MakeUpAccepted" : "MakeUpDeclined");
        Notify(session.PTUserId, "Đề xuất buổi bù PT", message, nameof(PTSession), session.Id);
        Notify(session.MemberUserId, "Đề xuất buổi bù PT", message, nameof(PTSession), session.Id);
        await db.SaveChangesAsync(ct);
        return valid ? Result<PTSessionResponse>.Success(SessionResponse(session, now)) : Conflict<PTSessionResponse>(message);
    }

    private static void SettleCredits(PTPurchase p)
    {
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        p.RemainingSessions = Math.Max(0, p.TotalSessions - used);
        if (p.RemainingSessions == 0 && reserved == 0)
        {
            p.Status = PTPurchaseStatus.Completed;
            p.IsCompleted = true;
        }
    }

    public Task<Result<PTSessionResponse>> RecordAttendance(string actor, int id, bool attended, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, true, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        var now = Now;
        var target = attended ? PTSessionStatus.Completed : PTSessionStatus.NoShow;
        if (s.SessionStatus == target)
            return Result<PTSessionResponse>.Success(SessionResponse(s, now));
        if (s.SessionStatus is not (PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested) ||
            Utc(s.SessionDate, s.EndTime) > now)
            return Conflict<PTSessionResponse>("Buổi chưa kết thúc hoặc không thể chấm công.");
        if (attended && !await db.MemberMemberships.AnyAsync(m =>
                m.MemberUserId == p.MemberUserId && m.PackageComponent.ComponentType == ComponentType.GYM &&
                (m.MembershipStatus == MembershipStatus.Active || m.MembershipStatus == MembershipStatus.Expired) &&
                m.StartDate <= s.SessionDate && m.EndDate >= s.SessionDate &&
                !m.MembershipFreezes.Any(f => (f.RequestStatus == FreezeRequestStatus.Approved ||
                    f.RequestStatus == FreezeRequestStatus.Active || f.RequestStatus == FreezeRequestStatus.Completed) &&
                    (f.ActualStartDate ?? f.RequestedStartDate) <= s.SessionDate &&
                    (f.ActualEndDate ?? f.RequestedEndDate) > s.SessionDate), ct))
            return Result<PTSessionResponse>.Failure("Cần gói Gym bao phủ ngày tập.", "PT_GYM_COVERAGE", 409);
        if (s.SessionStatus == PTSessionStatus.RescheduleRequested)
            CloseMemberReschedule(s, null, "RescheduleExpired", "Yêu cầu đổi lịch đã hết hạn.");
        else
            ClearProposal(s);
        s.SessionStatus = target;
        SettleCredits(p);
        Audit(actor, nameof(PTSession), s.Id, target.ToString());
        await db.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(s, now));
    }, ct);

    public Task<Result<PTSessionResponse>> Note(string actor, int id, PTNoteRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var session = await db.PTSessions.SingleOrDefaultAsync(s => s.Id == id && s.PTUserId == actor, ct);
        if (session is null)
            return Missing<PTSessionResponse>();
        if (session.SessionStatus != PTSessionStatus.Completed || string.IsNullOrWhiteSpace(request.Note))
            return Invalid<PTSessionResponse>("Chỉ ghi chú cho buổi đã hoàn thành.");
        session.Note = request.Note.Trim();
        session.NoteUpdatedAt = Now;
        Audit(actor, nameof(PTSession), session.Id, "CoachingNoteUpdated");
        await db.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, Now));
    }, ct);

    public Task<Result<int>> Review(string actor, int id, PTReviewRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<int>();
        var p = await ContractsWithSessions.SingleOrDefaultAsync(p => p.Id == id && p.MemberUserId == actor, ct);
        if (p is null)
            return Missing<int>();
        var last = p.Sessions.Where(s => s.SessionStatus != PTSessionStatus.MakeUpPending &&
                (s.SessionStatus != PTSessionStatus.Cancelled || s.IsCreditForfeited))
            .OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.StartTime).FirstOrDefault();
        if (EffectiveStatus(p) != PTPurchaseStatus.Completed || last?.SessionStatus != PTSessionStatus.Completed ||
            await db.TrainerReviews.AnyAsync(r => r.PTPurchaseId == id, ct))
            return Conflict<int>("Hợp đồng chưa đủ điều kiện hoặc đã được đánh giá.");
        var comment = request.Comment.Trim();
        if (request.Rating is < 1 or > 5 || comment.Length is < 10 or > 500 ||
            (await bannedWords.FindBannedWordsAsync(comment, ct)).Count != 0)
            return Invalid<int>("Điểm hoặc nội dung đánh giá không hợp lệ.");
        var review = new TrainerReview
        {
            PTPurchaseId = p.Id, MemberUserId = actor, TrainerUserId = p.PTUserId,
            Rating = request.Rating, Comment = comment, ReviewStatus = TrainerReviewStatus.Approved, IsVisible = true
        };
        db.TrainerReviews.Add(review);
        await db.SaveChangesAsync(ct);
        return Result<int>.Success(review.Id, 201);
    }, ct);
}
