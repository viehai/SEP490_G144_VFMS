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
        var proposed = s.SessionStatus == PTSessionStatus.ReschedulePending && s.ProposalExpiresAt > now;
        return new(s.Id, s.SessionDate, s.StartTime, s.EndTime,
            (s.SessionStatus == PTSessionStatus.ReschedulePending && !proposed ? PTSessionStatus.Scheduled : s.SessionStatus).ToString(),
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
        PTAdjustmentType type, DateTime now, CancellationToken ct)
    {
        var units = type == PTAdjustmentType.DoubleSession ? 2 : s.CreditUnits;
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        if (EffectiveStatus(p) != PTPurchaseStatus.Active || date < p.StartDate || date > p.EndDate ||
            Utc(date, start) <= now || !InHours(p.PTUser.Gym, start, end) ||
            p.TotalSessions - used - reserved < units - s.CreditUnits ||
            p.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
                f.RequestedStartDate <= date && date < (f.ActualEndDate ?? f.RequestedEndDate)) ||
            !await Coverage(p.MemberUserId).AnyAsync(m => m.StartDate <= Today && m.EndDate >= date, ct))
            return false;
        return !await HasConflict(p.PTUserId, p.MemberUserId, [new(0, date, start, end, "Available")],
            now, ct, ignoreSession: s.Id, allowSecondMemberSession: true);
    }

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
        if (!Enum.TryParse<PTAdjustmentType>(request.Type, true, out var type) ||
            !string.Equals(request.Type, type.ToString(), StringComparison.OrdinalIgnoreCase))
            return Invalid<PTSessionResponse>("Loại đề xuất không hợp lệ.");
        if (type == PTAdjustmentType.DoubleSession && (s.CreditUnits != 1 ||
            request.Date != s.SessionDate || request.StartTime != s.StartTime))
            return Invalid<PTSessionResponse>("Ca đôi kéo dài buổi 60 phút hiện tại.");
        if (type == PTAdjustmentType.Reschedule && request.Date == s.SessionDate && request.StartTime == s.StartTime)
            return Invalid<PTSessionResponse>("Chọn lịch khác với lịch hiện tại.");
        var end = request.StartTime.AddHours(type == PTAdjustmentType.DoubleSession ? 2 : s.CreditUnits);
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
            $"Lịch đề xuất {request.Date:dd/MM/yyyy} {request.StartTime:HH:mm}–{end:HH:mm}. Hủy trước ít nhất 4 giờ để giữ credit.",
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

    public Task<Result<PTSessionResponse>> Cancel(string actor, int id, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, false, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        var now = Now;
        var start = Utc(s.SessionDate, s.StartTime);
        if (s.SessionStatus == PTSessionStatus.Cancelled)
            return Result<PTSessionResponse>.Success(SessionResponse(s, now));
        if (s.SessionStatus is not (PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending) || start <= now)
            return Conflict<PTSessionResponse>("Buổi này không thể hủy.");
        ClearProposal(s);
        s.SessionStatus = PTSessionStatus.Cancelled;
        s.CancelledAt = now;
        s.IsCreditForfeited = start - now < TimeSpan.FromHours(4);
        SettleCredits(p);
        Audit(actor, nameof(PTSession), s.Id, s.IsCreditForfeited ? "LateCancellation" : "Cancellation");
        await db.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(s, now));
    }, ct);

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
        if (s.SessionStatus is not (PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending) ||
            Utc(s.SessionDate, s.EndTime) > now)
            return Conflict<PTSessionResponse>("Buổi chưa kết thúc hoặc không thể chấm công.");
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
        var last = p.Sessions.Where(s => s.SessionStatus != PTSessionStatus.Cancelled || s.IsCreditForfeited)
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
