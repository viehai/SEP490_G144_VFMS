using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    public async Task<Result<IReadOnlyList<PTTeachingSlot>>> TeachingSchedule(string actor, DateOnly from, CancellationToken ct)
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<IReadOnlyList<PTTeachingSlot>>();
        if (from.DayOfWeek != DayOfWeek.Monday || from.DayNumber > DateOnly.MaxValue.DayNumber - 6)
            return Invalid<IReadOnlyList<PTTeachingSlot>>("Chọn ngày thứ Hai bắt đầu tuần.");

        var through = from.AddDays(6);
        var sessions = await store.Query<PTSession>().AsNoTracking(store)
            .Where(s => s.PTUserId == actor && s.SessionDate >= from && s.SessionDate <= through &&
                s.SessionStatus != PTSessionStatus.CheckoutHold && s.SessionStatus != PTSessionStatus.Cancelled &&
                s.SessionStatus != PTSessionStatus.MakeUpPending && s.SessionStatus != PTSessionStatus.MakeUpRequested &&
                s.SessionStatus != PTSessionStatus.FreezeHold)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime)
            .Select(s => new { Session = s, MemberName = s.Member.FullName }).ToListAsync(store, ct);
        var now = Now;
        return Result<IReadOnlyList<PTTeachingSlot>>.Success(sessions.Select(row => new PTTeachingSlot(
            row.Session.Id, row.Session.SessionDate, row.Session.StartTime, row.Session.EndTime,
            row.MemberName, SessionResponse(row.Session, now).Status, row.Session.PTPurchaseId)).ToArray());
    }

    private IQueryable<PTPurchase> ContractsWithSessions => store.Query<PTPurchase>()
        .Including(store, "Sessions", "PTFreezes", "PTUser.Gym", "Member");

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
        // GBR-14: after the end date the contract stays usable only for make-up units, for 14 more days.
        if (Today <= p.EndDate || Today <= PersonalTrainingRules.MakeUpDeadline(p.EndDate) && MakeUpUnits(p).Count > 0)
            return PTPurchaseStatus.Active;
        return PTPurchaseStatus.Expired;
    }

    /// <summary>A cancelled session returned without penalty that has no live replacement yet.</summary>
    private List<PTSession> MakeUpUnits(PTPurchase p) => p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.Cancelled &&
        s.CancelledAt != null && !s.IsCreditForfeited && !HasLiveReplacement(p, s.Id)).ToList();

    private bool HasLiveReplacement(PTPurchase p, int sessionId) => p.Sessions.Any(r => r.ReplacementForSessionId == sessionId &&
        (r.SessionStatus is PTSessionStatus.MakeUpPending or PTSessionStatus.MakeUpRequested
            ? r.ProposalExpiresAt > Now
            : r.SessionStatus != PTSessionStatus.Cancelled));

    private static PTSessionResponse SessionResponse(PTSession s, DateTime now)
    {
        var awaitingChange = s.SessionStatus is PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested;
        var proposed = awaitingChange && s.ProposalExpiresAt > now;
        if (s.SessionStatus is PTSessionStatus.MakeUpPending or PTSessionStatus.MakeUpRequested)
        {
            var pending = s.ProposalExpiresAt is { } deadline && deadline > now;
            return new(s.Id, s.SessionDate, s.StartTime, s.EndTime,
                pending ? s.SessionStatus.ToString() : nameof(PTSessionStatus.Cancelled),
                s.CreditUnits, s.Note, s.NoteUpdatedAt, pending
                    ? new(s.SessionDate, s.StartTime, s.EndTime, "MakeUp",
                        DateTime.SpecifyKind(s.ProposalExpiresAt!.Value, DateTimeKind.Utc)) : null,
                s.TrainerAbsenceReportedAt, s.TrainerAbsenceApproved, s.TrainerAbsenceReviewNote);
        }
        return new(s.Id, s.SessionDate, s.StartTime, s.EndTime,
            (awaitingChange && !proposed ? PTSessionStatus.Scheduled : s.SessionStatus).ToString(),
            s.CreditUnits, s.Note, s.NoteUpdatedAt, proposed
                ? new(s.ProposedDate!.Value, s.ProposedStartTime!.Value, s.ProposedEndTime!.Value,
                    s.ProposedAdjustmentType!.Value.ToString(),
                    DateTime.SpecifyKind(s.ProposalExpiresAt!.Value, DateTimeKind.Utc)) : null,
            s.TrainerAbsenceReportedAt, s.TrainerAbsenceApproved, s.TrainerAbsenceReviewNote);
    }

    private PTContractResponse ContractResponse(PTPurchase p)
    {
        var status = EffectiveStatus(p);
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        if (status == PTPurchaseStatus.PendingPayment)
            reserved = p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.CheckoutHold).Sum(s => s.CreditUnits);
        return new(p.Id, p.PTUserId, p.MemberUserId, p.PackageNameSnapshot, status.ToString(), p.StartDate, p.EndDate,
            p.TotalSessions, used, reserved, status == PTPurchaseStatus.Expired ? 0 : Math.Max(0, p.TotalSessions - used - reserved),
            Math.Max(0, p.TotalSessions - used),
            p.FreezeMinimumDaysSnapshot, p.FreezeMaxTotalDaysSnapshot, RemainingFreezeDays(p),
            p.Sessions.OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime)
                .Select(s => SessionResponse(s, Now) with { IsMakeUpUnit = MakeUpUnits(p).Contains(s) }).ToArray(),
            status == PTPurchaseStatus.PendingPayment ? p.Order?.OrderNumber : null,
            status == PTPurchaseStatus.PendingPayment && p.Order is { } order
                ? DateTime.SpecifyKind(PaymentDeadline(order), DateTimeKind.Utc) : null,
            p.PTUser.FullName, p.PTFreezes.Any(f => f.RequestStatus == PTFreezeRequestStatus.Pending || Approved(f)),
            p.Member.FullName)
        {
            MakeUpUnits = MakeUpUnits(p).Count,
            MakeUpDeadline = MakeUpUnits(p).Count > 0 ? PersonalTrainingRules.MakeUpDeadline(p.EndDate) : null,
            FreezeUsed = p.PTFreezes.Any(Approved),
            TrainerChangeDeadline = p.TrainerChangeDeadline > Now
                ? DateTime.SpecifyKind(p.TrainerChangeDeadline.Value, DateTimeKind.Utc) : null,
            GymOpenTime = p.PTUser.Gym?.OpenTime,
            GymCloseTime = p.PTUser.Gym?.CloseTime
        };
    }

    public async Task<Result<IReadOnlyList<PTContractResponse>>> MyContracts(string actor, int? id, CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null && await User(actor, UserRole.PT, ct) is null)
            return Forbidden<IReadOnlyList<PTContractResponse>>();
        var contracts = await VisibleContracts(actor).Where(p => id == null || p.Id == id)
            .OrderByDescending(p => p.Id).Take(100).ToListAsync(store, ct);
        var reviewed = await ReviewedContracts(actor, ct);
        return id != null && contracts.Count == 0 ? Missing<IReadOnlyList<PTContractResponse>>() :
            Result<IReadOnlyList<PTContractResponse>>.Success(contracts
                .Select(p => ContractResponse(p) with { HasReview = reviewed.Contains(p.Id) }).ToArray());
    }

    // UC 4.10: contracts this Member has already reviewed.
    private async Task<HashSet<int>> ReviewedContracts(string memberId, CancellationToken ct) =>
        (await store.Query<TrainerReview>().Where(r => r.MemberUserId == memberId && r.PTPurchaseId != null)
            .Select(r => r.PTPurchaseId!.Value).ToListAsync(store, ct)).ToHashSet();

    private IQueryable<PTPurchase> VisibleContracts(string actor) => ContractsWithSessions.AsNoTracking(store).Including(store, "Order")
        .Where(p => p.MemberUserId == actor || p.PTUserId == actor && p.Status != PTPurchaseStatus.PendingPayment &&
            (p.OrderId == null || p.Order!.OrderStatus == OrderStatus.Paid));

    public async Task<Result<PagedResponse<PTContractResponse>>> ContractPage(string actor, int page, string? trainerId,
        CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PagedResponse<PTContractResponse>>();
        var query = VisibleContracts(actor).Where(p => p.MemberUserId == actor &&
            (trainerId == null || p.PTUserId == trainerId));
        var reviewed = await ReviewedContracts(actor, ct);
        return await PageOf(query.OrderByDescending(p => p.Id), page,
            p => ContractResponse(p) with { HasReview = reviewed.Contains(p.Id) }, ct);
    }

    public async Task<Result<PagedResponse<PTAssignedMember>>> AssignedPage(string actor, string? search, bool historical,
        int page, CancellationToken ct)
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PagedResponse<PTAssignedMember>>();
        return await PageOf(AssignedContracts(actor, search, historical), page, AssignedResponse, ct);
    }

    private IQueryable<PTPurchase> AssignedContracts(string actor, string? search, bool historical)
    {
        // UC 4.6 / GBR-14: a contract stays 'Currently Assigned' until its 14-day make-up period ends.
        var makeUpFrom = Today.AddDays(-PersonalTrainingRules.MakeUpGraceDays);
        var query = VisibleContracts(actor).Where(p => p.PTUserId == actor &&
            (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen ||
             p.Status == PTPurchaseStatus.Completed || p.Status == PTPurchaseStatus.Expired));
        query = historical
            ? query.Where(p => p.EndDate < makeUpFrom || p.IsCompleted || p.Status == PTPurchaseStatus.Completed || p.Status == PTPurchaseStatus.Expired)
            : query.Where(p => p.EndDate >= makeUpFrom && !p.IsCompleted &&
                (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Member.FullName.Contains(search));
        return query.OrderBy(p => p.Member.FullName).ThenByDescending(p => p.Id);
    }

    private static PTAssignedMember AssignedResponse(PTPurchase p) => new(p.Id, p.MemberUserId,
            p.Member.FullName, p.Member.AvatarUrl, p.Member.Email, p.Member.PhoneNumber, p.TotalSessions,
            p.TotalSessions - PersonalTrainingRules.Credits(p.Sessions).Used, p.EndDate,
            p.Sessions.Where(s => s.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.AutoCompleted)
                .Max(s => (DateOnly?)s.SessionDate));

    private async Task<Result<PagedResponse<TResponse>>> PageOf<T, TResponse>(IQueryable<T> query, int page,
        Func<T, TResponse> map, CancellationToken ct)
    {
        const int pageSize = 20;
        var total = await query.CountAsync(store, ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling((double)total / pageSize)));
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(store, ct);
        return Result<PagedResponse<TResponse>>.Success(new(page, pageSize, total, rows.Select(map).ToArray()));
    }

    private Task<PTPurchase?> ContractForSession(string actor, int id, bool trainer, CancellationToken ct) =>
        ContractsWithSessions.SingleOrDefaultAsync(store, p => (trainer ? p.PTUserId == actor : p.MemberUserId == actor) &&
            p.Sessions.Any(s => s.Id == id), ct);

    private static void ClearProposal(PTSession s)
    {
        s.ProposedDate = null;
        s.ProposedStartTime = null;
        s.ProposedEndTime = null;
        s.ProposedAdjustmentType = null;
        s.ProposalExpiresAt = null;
        s.SessionStatus = PTSessionStatus.Scheduled;
    }

    private async Task<bool> CanAdjust(PTPurchase p, PTSession s, DateOnly date, TimeOnly start, TimeOnly end,
        DateTime now, CancellationToken ct, bool isMakeUp = false)
    {
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        var lastDay = isMakeUp ? PersonalTrainingRules.MakeUpDeadline(p.EndDate) : p.EndDate;
        if (EffectiveStatus(p) != PTPurchaseStatus.Active || date < p.StartDate || date > lastDay ||
            Utc(date, start) <= now || !InHours(p.PTUser.Gym, start, end) ||
            p.TotalSessions - used - reserved < (isMakeUp ? s.CreditUnits : 0) ||
            p.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
                f.RequestedStartDate <= date && date < (f.ActualEndDate ?? f.RequestedEndDate)))
            return false;
        // GBR-10: Gym access matters only when attendance is recorded, so a new date may fall after the membership ends.
        return !await HasConflict(p.PTUserId, p.MemberUserId, [new(0, date, start, end, "Available")],
            now, ct, ignoreSession: s.Id, allowSecondMemberSession: true);
    }

    // GBR-14: requests are sent at least 12 hours ahead and answered no later than 4 hours before the start.
    private static readonly TimeSpan RequestLeadTime = TimeSpan.FromHours(12);
    private static readonly TimeSpan ResponseCutoff = TimeSpan.FromHours(4);

    private Result<T>? LeadTimeError<T>(PTSession session, DateTime now) =>
        Utc(session.SessionDate, session.StartTime) - now < RequestLeadTime
            ? Result<T>.Failure("Yêu cầu đổi hoặc hủy buổi phải gửi trước giờ tập ít nhất 12 giờ.", "PT_LEAD_TIME", 409)
            : null;

    private static DateTime ResponseDeadline(DateTime start) => start - ResponseCutoff;

    /// <summary>UC 4.7: make-up from an available make-up unit; the Member or the assigned PT proposes, the other party decides.</summary>
    public Task<Result<PTSessionResponse>> ProposeMakeUp(string actor, int id, PTMakeUpRequest request, CancellationToken ct) =>
        Write(async () =>
        {
            var asTrainer = await User(actor, UserRole.PT, ct) is not null;
            if (!asTrainer && await User(actor, UserRole.Member, ct) is null)
                return Forbidden<PTSessionResponse>();
            var contract = await ContractForSession(actor, id, asTrainer, ct);
            if (contract is null)
                return Missing<PTSessionResponse>();

            var original = contract.Sessions.Single(s => s.Id == id);
            var now = Now;
            if (!MakeUpUnits(contract).Contains(original))
                return Conflict<PTSessionResponse>("Buổi này không có lượt tập bù hoặc đã có buổi bù đang chờ.");

            var session = new PTSession
            {
                MemberUserId = contract.MemberUserId,
                PTUserId = contract.PTUserId,
                SessionDate = request.Date,
                StartTime = request.StartTime,
                EndTime = request.StartTime.AddHours(1),
                CreditUnits = 1,
                SessionStatus = asTrainer ? PTSessionStatus.MakeUpPending : PTSessionStatus.MakeUpRequested,
                ReplacementForSessionId = id
            };
            if (!await CanAdjust(contract, session, session.SessionDate, session.StartTime, session.EndTime,
                now, ct, isMakeUp: true))
                return Conflict<PTSessionResponse>("Lịch không khả dụng, ngoài hạn tập bù hoặc không đủ số buổi.");

            session.ProposalExpiresAt = new[] { now.AddHours(24), Utc(session.SessionDate, session.StartTime) }.Min();
            contract.Sessions.Add(session);
            await store.Save(ct);
            Audit(actor, nameof(PTSession), session.Id, asTrainer ? "MakeUpProposed" : "MakeUpRequested");
            Notify(asTrainer ? session.MemberUserId : session.PTUserId, "Đề xuất buổi bù PT",
                $"Buổi bù đề xuất {session.SessionDate:dd/MM/yyyy} {session.StartTime:HH:mm}–{session.EndTime:HH:mm}.",
                nameof(PTSession), session.Id);
            await store.Save(ct);
            return Result<PTSessionResponse>.Success(SessionResponse(session, now), 201);
        }, ct);

    /// <summary>UC 4.7: the PT proposes a reschedule; the Member decides.</summary>
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
            return Conflict<PTSessionResponse>("Chỉ có thể đề xuất cho buổi sắp tới chưa có yêu cầu chờ.");
        if (LeadTimeError<PTSessionResponse>(s, now) is { } late)
            return late;
        // UC 4.7: only a reschedule; the double session was removed from the spec.
        if (!Enum.TryParse<PTAdjustmentType>(request.Type, true, out var type) || type != PTAdjustmentType.Reschedule)
            return Invalid<PTSessionResponse>("Loại đề xuất không hợp lệ.");
        if (IsOriginalTime(s, request.Date, request.StartTime) || !IsWholeHour(request.StartTime))
            return Invalid<PTSessionResponse>("Chọn giờ bắt đầu tròn giờ, khác lịch hiện tại.");
        var end = request.StartTime.Add(s.EndTime - s.StartTime);
        if (!await CanAdjust(p, s, request.Date, request.StartTime, end, now, ct))
            return Conflict<PTSessionResponse>("Lịch không khả dụng hoặc không đủ số buổi.");
        s.ProposedDate = request.Date;
        s.ProposedStartTime = request.StartTime;
        s.ProposedEndTime = end;
        s.ProposedAdjustmentType = type;
        s.ProposalExpiresAt = ResponseDeadline(oldStart);
        s.SessionStatus = PTSessionStatus.ReschedulePending;
        Audit(actor, nameof(PTSession), s.Id, $"Proposed:{type}:{request.Date:yyyy-MM-dd}:{request.StartTime:HH:mm}");
        Notify(s.MemberUserId, "PT đề nghị đổi lịch",
            $"Lịch đề xuất {request.Date:dd/MM/yyyy} {request.StartTime:HH:mm}–{end:HH:mm}.",
            nameof(PTSession), s.Id);
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(s, now));
    }, ct);

    /// <summary>UC 4.7: either party asks to cancel an upcoming session; the other party accepts or declines.</summary>
    public Task<Result<PTSessionResponse>> RequestCancellation(string actor, int id, CancellationToken ct) => Write(async () =>
    {
        var asTrainer = await User(actor, UserRole.PT, ct) is not null;
        if (!asTrainer && await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTSessionResponse>();
        var contract = await ContractForSession(actor, id, asTrainer, ct);
        if (contract is null)
            return Missing<PTSessionResponse>();
        var session = contract.Sessions.Single(s => s.Id == id);
        var now = Now;
        if (session.SessionStatus != PTSessionStatus.Scheduled || Utc(session.SessionDate, session.StartTime) <= now)
            return Conflict<PTSessionResponse>("Chỉ có thể xin hủy buổi sắp tới chưa có yêu cầu chờ.");
        if (LeadTimeError<PTSessionResponse>(session, now) is { } late)
            return late;
        session.ProposedDate = session.SessionDate;
        session.ProposedStartTime = session.StartTime;
        session.ProposedEndTime = session.EndTime;
        session.ProposedAdjustmentType = PTAdjustmentType.Cancellation;
        session.ProposalExpiresAt = ResponseDeadline(Utc(session.SessionDate, session.StartTime));
        session.SessionStatus = asTrainer ? PTSessionStatus.ReschedulePending : PTSessionStatus.RescheduleRequested;
        Audit(actor, nameof(PTSession), id, "CancellationRequested");
        Notify(asTrainer ? session.MemberUserId : session.PTUserId, "Yêu cầu hủy buổi PT",
            $"{(asTrainer ? "PT" : "Hội viên")} đề nghị hủy buổi {session.SessionDate:dd/MM/yyyy} {session.StartTime:HH:mm}. " +
            "Nếu đồng ý, buổi được trả lại thành buổi bù.", nameof(PTSession), id);
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    private void AcceptCancellation(PTSession session, PTPurchase contract, string actor, DateTime now)
    {
        ClearProposal(session);
        MarkMakeUp(session, now);
        var message = $"Buổi {session.SessionDate:dd/MM/yyyy} {session.StartTime:HH:mm} đã được hủy theo thỏa thuận và trả lại thành buổi bù " +
            $"(dùng được đến {PersonalTrainingRules.MakeUpDeadline(contract.EndDate):dd/MM/yyyy}).";
        Audit(actor, nameof(PTSession), session.Id, "CancellationAccepted");
        Notify(session.MemberUserId, "Đã hủy buổi PT", message, nameof(PTSession), session.Id);
        Notify(session.PTUserId, "Đã hủy buổi PT", message, nameof(PTSession), session.Id);
    }

    /// <summary>The Member answers a PT proposal: reschedule, cancellation or make-up.</summary>
    public Task<Result<PTSessionResponse>> DecideProposal(string actor, int id, PTProposalDecision request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, false, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        if (s.SessionStatus == PTSessionStatus.MakeUpPending)
            return await DecideMakeUp(p, s, actor, request.Accept, request.ProposalExpiresAt, ct);
        if (s.SessionStatus != PTSessionStatus.ReschedulePending || s.ProposalExpiresAt != request.ProposalExpiresAt)
            return Conflict<PTSessionResponse>("Đề xuất đã thay đổi. Vui lòng tải lại.");
        var now = Now;
        var valid = s.ProposalExpiresAt > now;
        var type = s.ProposedAdjustmentType!.Value;
        if (valid && request.Accept && type == PTAdjustmentType.Cancellation)
        {
            AcceptCancellation(s, p, actor, now);
            await store.Save(ct);
            return Result<PTSessionResponse>.Success(SessionResponse(s, now));
        }
        // A double-session proposal left from before its removal can no longer be accepted.
        if (type == PTAdjustmentType.DoubleSession)
            valid = false;
        if (request.Accept && valid)
        {
            valid = await CanAdjust(p, s, s.ProposedDate!.Value, s.ProposedStartTime!.Value,
                s.ProposedEndTime!.Value, now, ct);
            if (valid)
            {
                s.SessionDate = s.ProposedDate.Value;
                s.StartTime = s.ProposedStartTime.Value;
                s.EndTime = s.ProposedEndTime.Value;
                s.PTAvailabilityId = await Templates(s.PTUserId).Where(x =>
                            x.DayOfWeek == s.SessionDate.DayOfWeek && x.StartTime == s.StartTime)
                        .Select(x => (int?)x.Id).FirstOrDefaultAsync(store, ct);
            }
        }
        ClearProposal(s);
        var outcome = !valid ? "ProposalUnavailable" : request.Accept ? "ProposalAccepted" : "ProposalDeclined";
        Audit(actor, nameof(PTSession), s.Id, outcome);
        var message = !valid ? "Đề xuất không còn khả dụng hoặc đã quá hạn trả lời. Lịch cũ được giữ nguyên." :
            request.Accept ? "Hội viên đã đồng ý thay đổi lịch PT." : "Hội viên đã từ chối. Lịch cũ được giữ nguyên.";
        Notify(s.PTUserId, "Đề xuất lịch PT", message, nameof(PTSession), s.Id);
        Notify(s.MemberUserId, "Đề xuất lịch PT", message, nameof(PTSession), s.Id);
        await store.Save(ct);
        return valid ? Result<PTSessionResponse>.Success(SessionResponse(s, now)) : Conflict<PTSessionResponse>(message);
    }, ct);

    /// <summary>The PT answers a make-up appointment requested by the Member.</summary>
    public Task<Result<PTSessionResponse>> DecideMakeUpRequest(string actor, int id, PTProposalDecision request, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.PT, ct) is null)
                return Forbidden<PTSessionResponse>();
            var contract = await ContractForSession(actor, id, true, ct);
            if (contract is null)
                return Missing<PTSessionResponse>();
            var session = contract.Sessions.Single(s => s.Id == id);
            return session.SessionStatus == PTSessionStatus.MakeUpRequested
                ? await DecideMakeUp(contract, session, actor, request.Accept, request.ProposalExpiresAt, ct)
                : Conflict<PTSessionResponse>("Yêu cầu buổi bù đã thay đổi. Vui lòng tải lại.");
        }, ct);

    private async Task<Result<PTSessionResponse>> DecideMakeUp(PTPurchase contract, PTSession session, string actor,
        bool accept, DateTime proposalExpiresAt, CancellationToken ct)
    {
        if (session.ProposalExpiresAt != proposalExpiresAt)
            return Conflict<PTSessionResponse>("Đề xuất đã thay đổi. Vui lòng tải lại.");
        var now = Now;
        var valid = session.ProposalExpiresAt > now;
        if (valid && accept)
            valid = await CanAdjust(contract, session, session.SessionDate, session.StartTime, session.EndTime,
                now, ct, isMakeUp: true);

        ClearProposal(session);
        if (!valid || !accept)
            session.SessionStatus = PTSessionStatus.Cancelled;

        var message = !valid ? "Buổi bù không còn khả dụng. Lượt tập bù vẫn được giữ." :
            accept ? "Buổi bù PT đã được xác nhận." : "Đề xuất buổi bù bị từ chối. Lượt tập bù vẫn được giữ.";
        Audit(actor, nameof(PTSession), session.Id,
            !valid ? "MakeUpUnavailable" : accept ? "MakeUpAccepted" : "MakeUpDeclined");
        Notify(session.PTUserId, "Buổi bù PT", message, nameof(PTSession), session.Id);
        Notify(session.MemberUserId, "Buổi bù PT", message, nameof(PTSession), session.Id);
        await store.Save(ct);
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

    private static bool AwaitingAttendance(PTSessionStatus status) =>
        status is PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested;

    private Task<bool> HasGymAccess(string memberId, DateOnly date, CancellationToken ct) =>
        store.Query<MemberMembership>().AnyAsync(store, m =>
            m.MemberUserId == memberId && m.PackageComponent.ComponentType == ComponentType.GYM &&
            (m.MembershipStatus == MembershipStatus.Active || m.MembershipStatus == MembershipStatus.Expired) &&
            m.StartDate <= date && m.EndDate >= date &&
            !m.MembershipFreezes.Any(f => (f.RequestStatus == FreezeRequestStatus.Approved ||
                f.RequestStatus == FreezeRequestStatus.Active || f.RequestStatus == FreezeRequestStatus.Completed) &&
                (f.ActualStartDate ?? f.RequestedStartDate) <= date &&
                (f.ActualEndDate ?? f.RequestedEndDate) > date), ct);

    private async Task<List<string>> ManagerIds(string trainerId, CancellationToken ct)
    {
        var gymId = await store.Query<ApplicationUser>().Where(u => u.UserId == trainerId)
            .Select(u => (int?)u.GymId).FirstOrDefaultAsync(store, ct);
        return await ActiveUsers.Where(u => u.GymId == gymId && u.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.Manager)))
            .Select(u => u.UserId).ToListAsync(store, ct);
    }

    private static void MarkMakeUp(PTSession session, DateTime now)
    {
        session.SessionStatus = PTSessionStatus.Cancelled;
        session.CancelledAt ??= now;
        session.IsCreditForfeited = false;
    }

    private static void ReopenIfCompleted(PTPurchase contract, DateOnly today)
    {
        if (contract.Status == PTPurchaseStatus.Completed && today <= PersonalTrainingRules.MakeUpDeadline(contract.EndDate))
        {
            contract.Status = PTPurchaseStatus.Active;
            contract.IsCompleted = false;
        }
    }

    /// <summary>UC 4.6 / GBR-97: Present or Absent is recorded after the session ends, on the session day, and can be edited that day.</summary>
    public Task<Result<PTSessionResponse>> RecordAttendance(string actor, int id, bool attended, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, true, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        var now = Now;
        if (s.SessionDate != Today || Utc(s.SessionDate, s.EndTime) > now)
            return Conflict<PTSessionResponse>("Chỉ điểm danh sau khi buổi tập kết thúc và trong chính ngày tập.");
        if (attended && !await HasGymAccess(p.MemberUserId, s.SessionDate, ct))
            return Result<PTSessionResponse>.Failure(
                "Hội viên không có quyền vào gym trong ngày tập. Hãy ghi nhận vắng mặt.", "PT_GYM_COVERAGE", 409);

        var memberReported = s.TrainerAbsenceReportedAt != null && s.TrainerAbsenceReviewedAt == null;
        if (s.SessionStatus == PTSessionStatus.PendingFreeze)
        {
            // Protected by a pending freeze: attending uses the session, an absence is never penalised.
            if (attended)
                s.SessionStatus = PTSessionStatus.Completed;
        }
        else if (memberReported && s.SessionStatus is PTSessionStatus.Cancelled or PTSessionStatus.Disputed)
        {
            // UC 4.11: PT 'Present' against a Member 'PT Absent' report goes to the Manager.
            if (attended && s.SessionStatus != PTSessionStatus.Disputed)
            {
                s.SessionStatus = PTSessionStatus.Disputed;
                s.CancelledAt = null;
                foreach (var manager in await ManagerIds(actor, ct))
                    Notify(manager, "Tranh chấp điểm danh PT", "PT ghi có mặt nhưng hội viên báo PT vắng mặt.", nameof(PTSession), id);
            }
            else if (!attended)
                MarkMakeUp(s, now);
        }
        else if (AwaitingAttendance(s.SessionStatus) || s.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.NoShow)
        {
            if (s.SessionStatus == PTSessionStatus.RescheduleRequested)
                CloseMemberReschedule(s, null, "RescheduleExpired", "Yêu cầu đổi lịch đã hết hạn.");
            else if (AwaitingAttendance(s.SessionStatus))
                ClearProposal(s);
            // UC 4.8 step 8.1 / GBR-97: a session under a pending freeze is never penalised, even after a 'Present' is corrected.
            var pendingFreeze = s.SessionStatus == PTSessionStatus.Completed && p.PTFreezes.Any(f =>
                f.RequestStatus == PTFreezeRequestStatus.Pending && f.RequestedStartDate <= s.SessionDate && s.SessionDate < f.RequestedEndDate);
            s.SessionStatus = attended ? PTSessionStatus.Completed : pendingFreeze ? PTSessionStatus.PendingFreeze : PTSessionStatus.NoShow;
        }
        else
            return Conflict<PTSessionResponse>("Buổi này không thể điểm danh.");
        SettleCredits(p);
        Audit(actor, nameof(PTSession), s.Id, attended ? "AttendancePresent" : "AttendanceAbsent");
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(s, now));
    }, ct);

    /// <summary>UC 4.6 / GBR-97: on the session day the Member reports 'PT Absent'.</summary>
    public Task<Result<PTSessionResponse>> ReportTrainerAbsence(string actor, int id,
        PTTrainerAbsenceReportRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTSessionResponse>();
        var contract = await ContractForSession(actor, id, false, ct);
        if (contract is null)
            return Missing<PTSessionResponse>();
        var session = contract.Sessions.Single(s => s.Id == id);
        var now = Now;
        if (session.TrainerAbsenceReportedAt is not null)
            return Conflict<PTSessionResponse>("Buổi này đã được báo PT vắng mặt.");
        if (session.SessionDate != Today || Utc(session.SessionDate, session.StartTime) > now ||
            !(AwaitingAttendance(session.SessionStatus) || session.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.NoShow))
            return Conflict<PTSessionResponse>("Chỉ báo PT vắng mặt trong ngày tập, sau giờ bắt đầu buổi.");

        session.StatusBeforeTrainerAbsenceReport = session.SessionStatus;
        session.TrainerAbsenceReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        session.TrainerAbsenceReportedAt = now;
        string message;
        if (session.SessionStatus == PTSessionStatus.Completed)
        {
            session.SessionStatus = PTSessionStatus.Disputed;
            message = "PT đã ghi có mặt nên báo cáo được chuyển Manager xem xét.";
            foreach (var manager in await ManagerIds(session.PTUserId, ct))
                Notify(manager, "Tranh chấp điểm danh PT", "Hội viên báo PT vắng mặt nhưng PT đã ghi có mặt.", nameof(PTSession), id);
        }
        else
        {
            // PT recorded Absent or nothing: the session did not take place and becomes a make-up unit.
            if (AwaitingAttendance(session.SessionStatus))
                ClearProposal(session);
            MarkMakeUp(session, now);
            session.TrainerAbsenceApproved = true;
            ReopenIfCompleted(contract, Today);
            message = $"Buổi tập được trả lại thành buổi bù, dùng được đến {PersonalTrainingRules.MakeUpDeadline(contract.EndDate):dd/MM/yyyy}.";
        }
        SettleCredits(contract);
        Audit(actor, nameof(PTSession), id, $"TrainerAbsentReported:{session.SessionStatus}");
        Notify(session.PTUserId, "Hội viên báo PT vắng mặt", message, nameof(PTSession), id);
        Notify(actor, "Đã ghi nhận báo PT vắng mặt", message, nameof(PTSession), id);
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    /// <summary>UC 4.11: disputed sessions (processed = already decided).</summary>
    public async Task<Result<PagedResponse<PTAttendanceDispute>>> AttendanceDisputes(string actor,
        bool processed, int page, CancellationToken ct)
    {
        if (await User(actor, UserRole.Manager, ct) is null)
            return Forbidden<PagedResponse<PTAttendanceDispute>>();
        var query = store.Query<PTSession>().AsNoTracking(store).Including(store, "Member", "PTUser")
            .Where(s => s.TrainerAbsenceReportedAt != null && (processed
                ? s.TrainerAbsenceReviewedAt != null
                : s.SessionStatus == PTSessionStatus.Disputed))
            .OrderByDescending(s => s.TrainerAbsenceReportedAt);
        return await PageOf(query, page, s => new PTAttendanceDispute(s.Id, s.PTPurchaseId,
            s.Member.FullName, s.PTUser.FullName, s.SessionDate, s.StartTime, s.EndTime, s.TrainerAbsenceReason,
            s.TrainerAbsenceReportedAt!.Value, s.SessionStatus.ToString(),
            s.TrainerAbsenceReviewedAt == null ? null : DisputeDecision(s.SessionStatus), s.TrainerAbsenceReviewNote), ct);
    }

    private static string? DisputeDecision(PTSessionStatus status) => status switch
    {
        PTSessionStatus.Completed => PTDisputeDecisions.SessionHeld,
        PTSessionStatus.Cancelled => PTDisputeDecisions.PTAbsent,
        PTSessionStatus.NoShow => PTDisputeDecisions.MemberAbsent,
        _ => null
    };

    /// <summary>UC 4.11: the Manager decides Session Held, PT Absent or Member Absent.</summary>
    public Task<Result<PTSessionResponse>> DecideDispute(string actor, int id,
        PTAttendanceDisputeDecision request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Manager, ct) is null)
            return Forbidden<PTSessionResponse>();
        var session = await store.Query<PTSession>().Including(store, "PTPurchase.Sessions")
            .SingleOrDefaultAsync(store, s => s.Id == id, ct);
        if (session is null)
            return Missing<PTSessionResponse>();
        if (session.SessionStatus != PTSessionStatus.Disputed)
            return Conflict<PTSessionResponse>("Tranh chấp này đã được xử lý.");
        var now = Now;
        switch (request.Decision)
        {
            case PTDisputeDecisions.SessionHeld:
                session.SessionStatus = PTSessionStatus.Completed;
                break;
            case PTDisputeDecisions.PTAbsent:
                MarkMakeUp(session, now);
                ReopenIfCompleted(session.PTPurchase, Today);
                break;
            case PTDisputeDecisions.MemberAbsent:
                session.SessionStatus = PTSessionStatus.NoShow;
                break;
            default:
                return Invalid<PTSessionResponse>("Quyết định không hợp lệ.");
        }
        session.TrainerAbsenceApproved = request.Decision == PTDisputeDecisions.PTAbsent;
        session.TrainerAbsenceReviewNote = request.Note.Trim();
        session.TrainerAbsenceReviewedAt = now;
        SettleCredits(session.PTPurchase);
        Audit(actor, nameof(PTSession), id, $"DisputeDecided:{request.Decision}");
        var outcome = request.Decision switch
        {
            PTDisputeDecisions.SessionHeld => "Manager xác nhận buổi tập đã diễn ra; buổi được tính là đã dùng.",
            PTDisputeDecisions.PTAbsent => "Manager xác nhận PT vắng mặt; buổi tập được trả lại thành buổi bù.",
            _ => "Manager xác nhận hội viên vắng mặt; buổi tập bị tính là đã dùng."
        };
        Notify(session.MemberUserId, "Kết quả tranh chấp điểm danh", outcome, nameof(PTSession), id);
        Notify(session.PTUserId, "Kết quả tranh chấp điểm danh", outcome, nameof(PTSession), id);
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    /// <summary>GBR-97: 20:00 reminder for unrecorded sessions of the day; unrecorded past days become Completed (auto).</summary>
    public Task<Result<int>> ProcessAttendance(CancellationToken ct) => Write(async () =>
    {
        var now = Now;
        var today = Today;
        var changed = 0;
        var stale = await store.Query<PTSession>().Including(store, "PTPurchase.Sessions")
            .Where(s => s.SessionDate < today && (s.SessionStatus == PTSessionStatus.Scheduled ||
                s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.RescheduleRequested))
            .OrderBy(s => s.Id).Take(200).ToListAsync(store, ct);
        foreach (var session in stale)
        {
            ClearProposal(session);
            session.SessionStatus = PTSessionStatus.AutoCompleted;
            SettleCredits(session.PTPurchase);
            changed++;
        }
        if (now.AddHours(7).TimeOfDay >= TimeSpan.FromHours(20))
        {
            var unrecorded = await store.List(store.Query<PTSession>().Where(s => s.SessionDate == today &&
                s.AttendanceReminderSentAt == null && (s.SessionStatus == PTSessionStatus.Scheduled ||
                    s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.RescheduleRequested))
                .OrderBy(s => s.Id).Take(200), ct);
            foreach (var session in unrecorded.Where(s => Utc(s.SessionDate, s.EndTime) <= now))
            {
                session.AttendanceReminderSentAt = now;
                Notify(session.PTUserId, "Nhắc điểm danh PT",
                    $"Buổi {session.StartTime:HH:mm}–{session.EndTime:HH:mm} hôm nay chưa được điểm danh.", nameof(PTSession), session.Id);
                changed++;
            }
        }
        await store.Save(ct);
        return Result<int>.Success(changed);
    }, ct);

    public Task<Result<PTSessionResponse>> Note(string actor, int id, PTNoteRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var session = await store.Query<PTSession>().SingleOrDefaultAsync(store, s => s.Id == id && s.PTUserId == actor, ct);
        if (session is null)
            return Missing<PTSessionResponse>();
        if (session.SessionStatus is not (PTSessionStatus.Completed or PTSessionStatus.AutoCompleted) ||
            string.IsNullOrWhiteSpace(request.Note))
            return Invalid<PTSessionResponse>("Chỉ ghi chú cho buổi đã hoàn thành.");
        session.Note = request.Note.Trim();
        session.NoteUpdatedAt = Now;
        Audit(actor, nameof(PTSession), session.Id, "CoachingNoteUpdated");
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, Now));
    }, ct);

    public Task<Result<int>> Review(string actor, int id, PTReviewRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<int>();
        var p = await ContractsWithSessions.SingleOrDefaultAsync(store, p => p.Id == id && p.MemberUserId == actor, ct);
        if (p is null)
            return Missing<int>();
        var last = p.Sessions.Where(s => s.SessionStatus != PTSessionStatus.MakeUpPending &&
                (s.SessionStatus != PTSessionStatus.Cancelled || s.IsCreditForfeited))
            .OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.StartTime).FirstOrDefault();
        if (EffectiveStatus(p) != PTPurchaseStatus.Completed ||
            last?.SessionStatus is not (PTSessionStatus.Completed or PTSessionStatus.AutoCompleted) ||
            await store.Query<TrainerReview>().AnyAsync(store, r => r.PTPurchaseId == id, ct))
            return Result<int>.Failure("Chỉ hợp đồng đã hoàn thành đủ buổi và chưa được đánh giá mới có thể đánh giá.",
                "PT_REVIEW_NOT_ELIGIBLE", 409);
        var comment = request.Comment.Trim();
        if (request.Rating is < 1 or > 5 || comment.Length is < 10 or > 500)
            return Invalid<int>("Chọn từ 1 đến 5 sao và nhập nhận xét từ 10 đến 500 ký tự.");
        if (await bannedWords.FindBannedWordsAsync(comment, ct) is { Count: > 0 } banned)
            return Result<int>.Failure(TrainerContentRules.Message(banned), TrainerContentRules.RejectedCode, 400);
        var review = new TrainerReview
        {
            PTPurchaseId = p.Id,
            MemberUserId = actor,
            TrainerUserId = last.PTUserId,
            Rating = request.Rating,
            Comment = comment,
            ReviewStatus = TrainerReviewStatus.Approved,
            IsVisible = true
        };
        store.Add(review);
        await store.Save(ct);
        return Result<int>.Success(review.Id, 201);
    }, ct);
}
