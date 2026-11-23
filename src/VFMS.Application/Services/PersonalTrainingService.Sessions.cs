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
        var sessions = await repo.GetTeachingSessionsAsync(actor, from, through, ct);
        var now = Now;
        return Result<IReadOnlyList<PTTeachingSlot>>.Success(sessions.Select(s => new PTTeachingSlot(
            s.Id, s.SessionDate, s.StartTime, s.EndTime,
            s.Member.FullName, SessionResponse(s, now).Status, s.PTPurchaseId)).ToArray());
    }

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
        if (PersonalTrainingRules.IsStillRunning(p, Today))
            return PTPurchaseStatus.Active;
        return PTPurchaseStatus.Expired;
    }

    /// <summary>A cancelled session returned without penalty that has no live replacement yet. A unit from a
    /// 'PT Absent' report is usable only after the PT's response period (GBR-103).</summary>
    private List<PTSession> MakeUpUnits(PTPurchase p) => p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.Cancelled &&
        s.CancelledAt != null && !s.IsCreditForfeited && MakeUpAvailableAt(s) is null && !HasLiveReplacement(p, s.Id)).ToList();

    /// <summary>GBR-103: when a make-up unit from an undecided 'PT Absent' report becomes usable, or null if it already is.</summary>
    private DateTime? MakeUpAvailableAt(PTSession s)
    {
        if (s.SessionStatus != PTSessionStatus.Cancelled || s.TrainerAbsenceReportedAt is null || s.TrainerAbsenceReviewedAt is not null)
            return null;
        var opens = Utc(PersonalTrainingRules.TrainerResponseLastDay(s.SessionDate).AddDays(1), TimeOnly.MinValue);
        return opens > Now ? opens : null;
    }

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
            s.TrainerAbsenceReportedAt, s.TrainerAbsenceApproved, s.TrainerAbsenceReviewNote)
        {
            CompletedByCheckIn = CheckInOnly(s)
        };
    }

    /// <summary>UC 4.6 step 7: 'Present' set by the Member's check-in that no PT record has replaced.</summary>
    private static bool CheckInOnly(PTSession s) =>
        s.SessionStatus == PTSessionStatus.Completed && s.CheckInCompletedAt is not null && s.AttendanceRecordedAt is null;

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
                .Select(s => SessionResponse(s, Now) with
                {
                    IsMakeUpUnit = MakeUpUnits(p).Contains(s),
                    MakeUpAvailableAt = MakeUpAvailableAt(s) is { } opens ? DateTime.SpecifyKind(opens, DateTimeKind.Utc) : null
                }).ToArray(),
            status == PTPurchaseStatus.PendingPayment ? p.Order?.OrderNumber : null,
            status == PTPurchaseStatus.PendingPayment && p.Order is { } order
                ? DateTime.SpecifyKind(PaymentDeadline(order), DateTimeKind.Utc) : null,
            p.PTUser.FullName, p.PTFreezes.Any(f => f.RequestStatus == PTFreezeRequestStatus.Pending || Approved(f)),
            p.Member.FullName)
        {
            MakeUpUnits = MakeUpUnits(p).Count,
            // UC 4.5: also while a unit from a 'PT Absent' report waits for the PT's response period.
            MakeUpDeadline = MakeUpUnits(p).Count > 0 || p.Sessions.Any(s => MakeUpAvailableAt(s) is not null)
                ? PersonalTrainingRules.MakeUpDeadline(p.EndDate) : null,
            FreezeUsed = p.PTFreezes.Any(Approved),
            CanReview = FinalTaughtSession(p) is not null,
            TrainerLevel = p.TrainerLevelSnapshot?.ToString(),
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
        var contracts = await repo.GetVisibleContractsAsync(actor, Now, id, ct);
        var reviewed = await ReviewedContracts(actor, ct);
        return id != null && contracts.Count == 0 ? Missing<IReadOnlyList<PTContractResponse>>() :
            Result<IReadOnlyList<PTContractResponse>>.Success(contracts
                .Select(p => ContractResponse(p) with { HasReview = reviewed.Contains(p.Id) }).ToArray());
    }

    // UC 4.10: contracts this Member has already reviewed.
    private async Task<HashSet<int>> ReviewedContracts(string memberId, CancellationToken ct) =>
        (await repo.GetReviewedContractIdsAsync(memberId, ct)).ToHashSet();

    public async Task<Result<PagedResponse<PTContractResponse>>> ContractPage(string actor, int page, string? trainerId,
        CancellationToken ct, bool? completed = null)
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PagedResponse<PTContractResponse>>();
        // UC 4.10: 'My PT Packages' > 'Completed Contracts' lists only completed contracts.
        var contracts = await repo.GetMemberContractPageAsync(actor, Now, trainerId, completed, page, PageSize, ct);
        var reviewed = await ReviewedContracts(actor, ct);
        return PageOf(contracts, p => ContractResponse(p) with { HasReview = reviewed.Contains(p.Id) });
    }

    public async Task<Result<PagedResponse<PTAssignedMember>>> AssignedPage(string actor, string? search, bool historical,
        int page, CancellationToken ct)
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PagedResponse<PTAssignedMember>>();
        return PageOf(await repo.GetAssignedContractPageAsync(actor, Now, Today, search, historical, page, PageSize, ct),
            AssignedResponse);
    }

    private static PTAssignedMember AssignedResponse(PTPurchase p) => new(p.Id, p.MemberUserId,
            p.Member.FullName, p.Member.AvatarUrl, p.Member.Email, p.Member.PhoneNumber, p.TotalSessions,
            p.TotalSessions - PersonalTrainingRules.Credits(p.Sessions).Used, p.EndDate,
            p.Sessions.Where(s => s.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.AutoCompleted)
                .Max(s => (DateOnly?)s.SessionDate));

    private Task<PTPurchase?> ContractForSession(string actor, int id, bool trainer, CancellationToken ct) =>
        repo.GetContractForSessionAsync(id, actor, trainer, ct);

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
        DateTime now, CancellationToken ct, bool isMakeUp = false) =>
        await AdjustError<bool>(p, s, date, start, end, now, ct, isMakeUp) is null;

    private async Task<Result<T>?> AdjustError<T>(PTPurchase p, PTSession s, DateOnly date, TimeOnly start, TimeOnly end,
        DateTime now, CancellationToken ct, bool isMakeUp = false)
    {
        var (used, reserved) = PersonalTrainingRules.Credits(p.Sessions);
        var lastDay = isMakeUp ? PersonalTrainingRules.MakeUpDeadline(p.EndDate) : p.EndDate;
        if (EffectiveStatus(p) != PTPurchaseStatus.Active || date < p.StartDate || date > lastDay ||
            Utc(date, start) <= now || !InHours(p.PTUser.Gym, start, end) ||
            p.TotalSessions - used - reserved < (isMakeUp ? s.CreditUnits : 0) ||
            p.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
                f.RequestedStartDate <= date && date < (f.ActualEndDate ?? f.RequestedEndDate)))
            return Conflict<T>(isMakeUp
                ? "Lịch ngoài giờ gym, ngoài hạn tập bù, trong thời gian bảo lưu hoặc không đủ số buổi."
                : "Lịch ngoài giờ gym, sau ngày kết thúc hợp đồng hoặc trong thời gian bảo lưu.");
        // GBR-10: Gym access matters only when attendance is recorded, so a new date may fall after the membership ends.
        return await ScheduleConflict<T>(p, s.Id, date, start, end, now, ct);
    }

    // UC 4.7: MSG-32 when the Member has another PT/Yoga session, MSG-30 when the trainer is busy.
    private async Task<Result<T>?> ScheduleConflict<T>(PTPurchase p, int sessionId, DateOnly date, TimeOnly start,
        TimeOnly end, DateTime now, CancellationToken ct)
    {
        PTCalendarSlot[] slot = [new(0, date, start, end, "Available")];
        if (!await HasConflict(p.PTUserId, p.MemberUserId, slot, now, ct, ignoreSession: sessionId))
            return null;
        return await HasMemberBookingConflict(p.MemberUserId, slot, now, ct, sessionId)
            ? Result<T>.Failure("Hội viên đã có buổi PT hoặc lớp Yoga khác trong khung giờ này.", "PT_MEMBER_SCHEDULE_CONFLICT", 409)
            : Result<T>.Failure("Huấn luyện viên đã có lịch hoặc không rảnh trong khung giờ này.", "PT_TRAINER_SLOT_UNAVAILABLE", 409);
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
            if (!IsWholeHour(request.StartTime))
                return Invalid<PTSessionResponse>("Buổi bù phải bắt đầu vào giờ tròn.");
            if (await AdjustError<PTSessionResponse>(contract, session, session.SessionDate, session.StartTime,
                    session.EndTime, now, ct, isMakeUp: true) is { } error)
                return error;

            session.ProposalExpiresAt = new[] { now.AddHours(24), Utc(session.SessionDate, session.StartTime) }.Min();
            contract.Sessions.Add(session);
            await repo.SaveChangesAsync(ct);
            Audit(actor, nameof(PTSession), session.Id, asTrainer ? "MakeUpProposed" : "MakeUpRequested");
            Notify(asTrainer ? session.MemberUserId : session.PTUserId, "Đề xuất buổi bù PT",
                $"Buổi bù đề xuất {session.SessionDate:dd/MM/yyyy} {session.StartTime:HH:mm}–{session.EndTime:HH:mm}.",
                nameof(PTSession), session.Id);
            await repo.SaveChangesAsync(ct);
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
        if (await AdjustError<PTSessionResponse>(p, s, request.Date, request.StartTime, end, now, ct) is { } error)
            return error;
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
        await repo.SaveChangesAsync(ct);
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
        await repo.SaveChangesAsync(ct);
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
            await repo.SaveChangesAsync(ct);
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
                s.PTAvailabilityId = (await Templates(s.PTUserId, ct)).Where(x =>
                            x.DayOfWeek == s.SessionDate.DayOfWeek && x.StartTime == s.StartTime)
                        .Select(x => (int?)x.Id).FirstOrDefault();
            }
        }
        ClearProposal(s);
        var outcome = !valid ? "ProposalUnavailable" : request.Accept ? "ProposalAccepted" : "ProposalDeclined";
        Audit(actor, nameof(PTSession), s.Id, outcome);
        var message = !valid ? "Đề xuất không còn khả dụng hoặc đã quá hạn trả lời. Lịch cũ được giữ nguyên." :
            request.Accept ? "Hội viên đã đồng ý thay đổi lịch PT." : "Hội viên đã từ chối. Lịch cũ được giữ nguyên.";
        Notify(s.PTUserId, "Đề xuất lịch PT", message, nameof(PTSession), s.Id);
        Notify(s.MemberUserId, "Đề xuất lịch PT", message, nameof(PTSession), s.Id);
        await repo.SaveChangesAsync(ct);
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
        await repo.SaveChangesAsync(ct);
        return valid ? Result<PTSessionResponse>.Success(SessionResponse(session, now)) : Conflict<PTSessionResponse>(message);
    }

    private static void SettleCredits(PTPurchase p) => PersonalTrainingRules.Settle(p);

    private static bool AwaitingAttendance(PTSessionStatus status) =>
        status is PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested;

    private Task<bool> HasGymAccess(string memberId, DateOnly date, CancellationToken ct) =>
        repo.HasGymAccessOnAsync(memberId, date, ct);

    private Task<IReadOnlyList<string>> ManagerIds(string trainerId, CancellationToken ct) =>
        repo.GetGymManagerIdsAsync(trainerId, ct);

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

    /// <summary>UC 4.6 / GBR-103: Present or Absent is recorded after the session ends, on the session day, and can be edited
    /// that day. For a session the Member reported as 'PT Absent', the PT can still record it until the end of the next day.</summary>
    public Task<Result<PTSessionResponse>> RecordAttendance(string actor, int id, bool attended, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var p = await ContractForSession(actor, id, true, ct);
        if (p is null)
            return Missing<PTSessionResponse>();
        var s = p.Sessions.Single(x => x.Id == id);
        var now = Now;
        // UC 4.11: once disputed, the Manager reviews the PT's record as it stands.
        if (s.SessionStatus == PTSessionStatus.Disputed)
            return Conflict<PTSessionResponse>("Buổi đang chờ Manager xử lý tranh chấp, không sửa điểm danh được.");
        var memberReported = s.TrainerAbsenceReportedAt != null && s.TrainerAbsenceReviewedAt == null &&
            s.SessionStatus == PTSessionStatus.Cancelled;
        var lastDay = memberReported ? PersonalTrainingRules.TrainerResponseLastDay(s.SessionDate) : s.SessionDate;
        if (Today < s.SessionDate || Today > lastDay || Utc(s.SessionDate, s.EndTime) > now)
            return Conflict<PTSessionResponse>(memberReported
                ? "Chỉ điểm danh buổi bị hội viên báo vắng đến hết ngày hôm sau."
                : "Chỉ điểm danh sau khi buổi tập kết thúc và trong chính ngày tập.");
        if (attended && !await HasGymAccess(p.MemberUserId, s.SessionDate, ct))
            return Result<PTSessionResponse>.Failure(
                "Hội viên không có quyền vào gym trong ngày tập. Hãy ghi nhận vắng mặt.", "PT_GYM_COVERAGE", 409);

        if (s.SessionStatus == PTSessionStatus.PendingFreeze)
        {
            // Protected by a pending freeze: attending uses the session, an absence is never penalised.
            if (attended)
                s.SessionStatus = PTSessionStatus.Completed;
        }
        else if (memberReported)
        {
            // GBR-103: any PT record against the Member's 'PT Absent' report goes to the Manager (UC 4.11).
            s.SessionStatus = PTSessionStatus.Disputed;
            s.CancelledAt = null;
            s.TrainerAbsenceApproved = null;
            foreach (var manager in await ManagerIds(actor, ct))
                Notify(manager, "Tranh chấp điểm danh PT",
                    $"Hội viên báo PT vắng mặt nhưng PT ghi hội viên {(attended ? "có mặt" : "vắng mặt")}.", nameof(PTSession), id);
        }
        else if (AwaitingAttendance(s.SessionStatus) || s.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.NoShow)
        {
            if (s.SessionStatus == PTSessionStatus.RescheduleRequested)
                CloseMemberReschedule(s, null, "RescheduleExpired", "Yêu cầu đổi lịch đã hết hạn.");
            else if (AwaitingAttendance(s.SessionStatus))
                ClearProposal(s);
            // UC 4.8 step 8.1 / GBR-103: a session under a pending freeze is never penalised, even after a 'Present' is corrected.
            var pendingFreeze = s.SessionStatus == PTSessionStatus.Completed && p.PTFreezes.Any(f =>
                f.RequestStatus == PTFreezeRequestStatus.Pending && f.RequestedStartDate <= s.SessionDate && s.SessionDate < f.RequestedEndDate);
            s.SessionStatus = attended ? PTSessionStatus.Completed : pendingFreeze ? PTSessionStatus.PendingFreeze : PTSessionStatus.NoShow;
        }
        else
            return Conflict<PTSessionResponse>("Buổi này không thể điểm danh.");
        // UC 4.6 step 6: the PT's record replaces a 'Present' set by check-in.
        s.AttendanceRecordedAt = now;
        s.AttendanceRecordedPresent = attended;
        s.CheckInCompletedAt = null;
        SettleCredits(p);
        Audit(actor, nameof(PTSession), s.Id, attended ? "AttendancePresent" : "AttendanceAbsent");
        await repo.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(s, now));
    }, ct);

    /// <summary>UC 4.6 / GBR-103: on the session day the Member reports 'PT Absent'.</summary>
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
        string message, trainerMessage;
        // GBR-103: a PT record (Present or Absent) disagrees with the report; a 'Present' set only by check-in is not one.
        var trainerRecorded = session.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.NoShow && !CheckInOnly(session);
        if (trainerRecorded)
        {
            var recorded = session.SessionStatus == PTSessionStatus.Completed ? "có mặt" : "vắng mặt";
            session.SessionStatus = PTSessionStatus.Disputed;
            message = trainerMessage = $"PT đã ghi hội viên {recorded} nên báo cáo được chuyển Manager xem xét.";
            foreach (var manager in await ManagerIds(session.PTUserId, ct))
                Notify(manager, "Tranh chấp điểm danh PT", $"Hội viên báo PT vắng mặt nhưng PT đã ghi hội viên {recorded}.", nameof(PTSession), id);
        }
        else
        {
            // The PT recorded nothing: the session becomes a make-up unit, usable once the PT's response period ends.
            if (AwaitingAttendance(session.SessionStatus))
                ClearProposal(session);
            MarkMakeUp(session, now);
            session.TrainerAbsenceApproved = true;
            ReopenIfCompleted(contract, Today);
            var lastDay = PersonalTrainingRules.TrainerResponseLastDay(session.SessionDate);
            message = $"Buổi tập thành buổi bù. PT có thể phản hồi đến hết ngày {lastDay:dd/MM/yyyy}; sau đó bạn đặt lịch buổi bù được " +
                $"(dùng được đến {PersonalTrainingRules.MakeUpDeadline(contract.EndDate):dd/MM/yyyy}).";
            trainerMessage = $"Hội viên báo bạn vắng buổi {session.StartTime:HH:mm} ngày {session.SessionDate:dd/MM/yyyy}. " +
                $"Nếu bạn đã dạy, hãy điểm danh trước hết ngày {lastDay:dd/MM/yyyy} để Manager xem xét.";
        }
        SettleCredits(contract);
        Audit(actor, nameof(PTSession), id, $"TrainerAbsentReported:{session.SessionStatus}");
        Notify(session.PTUserId, "Hội viên báo PT vắng mặt", trainerMessage, nameof(PTSession), id);
        Notify(actor, "Đã ghi nhận báo PT vắng mặt", message, nameof(PTSession), id);
        await repo.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    /// <summary>UC 4.11: disputed sessions (processed = already decided).</summary>
    public async Task<Result<PagedResponse<PTAttendanceDispute>>> AttendanceDisputes(string actor,
        bool processed, int page, CancellationToken ct)
    {
        if (await User(actor, UserRole.Manager, ct) is null)
            return Forbidden<PagedResponse<PTAttendanceDispute>>();
        var disputes = await repo.GetDisputePageAsync(processed, page, PageSize, ct);
        // UC 4.11: the processed list names the Manager who decided; Manager accounts are few, so load them once.
        var names = processed ? await repo.GetManagerNamesAsync(ct) : new Dictionary<string, string>();
        return PageOf(disputes, s => new PTAttendanceDispute(s.Id, s.PTPurchaseId,
            s.Member.FullName, s.PTUser.FullName, s.SessionDate, s.StartTime, s.EndTime, s.TrainerAbsenceReason,
            s.TrainerAbsenceReportedAt!.Value, s.SessionStatus.ToString(),
            s.TrainerAbsenceReviewedAt == null ? null : DisputeDecision(s.SessionStatus), s.TrainerAbsenceReviewNote,
            s.TrainerAbsenceReviewedByUserId is { } reviewer ? names.GetValueOrDefault(reviewer) : null,
            // Older rows have no saved PT record: before this column, a dispute always came from a PT 'Present'.
            s.AttendanceRecordedPresent ?? (s.StatusBeforeTrainerAbsenceReport == PTSessionStatus.NoShow ? false : true),
            s.AttendanceRecordedAt is { } recordedAt ? DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc) : null));
    }

    /// <summary>UC 4.11 step 3: the Member's gym visits on the disputed session's day, marked when they match the session.</summary>
    public async Task<Result<IReadOnlyList<PTDisputeCheckIn>>> DisputeCheckIns(string actor, int id, CancellationToken ct)
    {
        if (await User(actor, UserRole.Manager, ct) is null)
            return Forbidden<IReadOnlyList<PTDisputeCheckIn>>();
        var session = await repo.GetReportedSessionAsync(id, ct);
        if (session is null)
            return Missing<IReadOnlyList<PTDisputeCheckIn>>();
        var dayStart = Utc(session.SessionDate, TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var visits = await repo.GetCheckInsAsync(session.MemberUserId, dayStart, dayEnd, ct);
        var start = Utc(session.SessionDate, session.StartTime);
        var end = Utc(session.SessionDate, session.EndTime);
        var utc = (DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);
        IReadOnlyList<PTDisputeCheckIn> rows = visits.Select(c => new PTDisputeCheckIn(
            utc(c.CheckinTime), c.CheckinMethod.ToString(), c.HandledByUser?.FullName,
            c.CheckoutTime is { } outAt ? utc(outAt) : null, c.CheckoutMethod?.ToString(), c.CheckedOutByUser?.FullName,
            c.Status.ToString(),
            // GBR-92 window: from 30 minutes before to 15 minutes after the start.
            c.CheckinTime >= start.AddMinutes(-30) && c.CheckinTime <= start.AddMinutes(15),
            c.CheckinTime < end && (c.CheckoutTime ?? (c.Status == CheckInStatus.Active ? Now : c.CheckinTime)) > start)).ToList();
        return Result<IReadOnlyList<PTDisputeCheckIn>>.Success(rows);
    }

    private static string? DisputeDecision(PTSessionStatus status) => status switch
    {
        PTSessionStatus.Completed => PTDisputeDecisions.SessionHeld,
        PTSessionStatus.Cancelled => PTDisputeDecisions.PTAbsent,
        PTSessionStatus.NoShow or PTSessionStatus.PendingFreeze => PTDisputeDecisions.MemberAbsent,
        _ => null
    };

    /// <summary>UC 4.11: the Manager decides Session Held, PT Absent or Member Absent.</summary>
    public Task<Result<PTSessionResponse>> DecideDispute(string actor, int id,
        PTAttendanceDisputeDecision request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Manager, ct) is null)
            return Forbidden<PTSessionResponse>();
        var session = await repo.GetSessionWithContractAsync(id, ct);
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
                // GBR-103: a session under a pending freeze is never penalised.
                session.SessionStatus = session.PTPurchase.PTFreezes.Any(f => f.RequestStatus == PTFreezeRequestStatus.Pending &&
                    f.RequestedStartDate <= session.SessionDate && session.SessionDate < f.RequestedEndDate)
                    ? PTSessionStatus.PendingFreeze : PTSessionStatus.NoShow;
                break;
            default:
                return Invalid<PTSessionResponse>("Quyết định không hợp lệ.");
        }
        session.TrainerAbsenceApproved = request.Decision == PTDisputeDecisions.PTAbsent;
        session.TrainerAbsenceReviewNote = request.Note.Trim();
        session.TrainerAbsenceReviewedAt = now;
        session.TrainerAbsenceReviewedByUserId = actor;
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
        await repo.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    /// <summary>GBR-103: sessions nobody recorded and no check-in completed become Completed (auto) after their day.</summary>
    public Task<Result<int>> ProcessAttendance(CancellationToken ct) => Write(async () =>
    {
        var today = Today;
        var changed = 0;
        var stale = await repo.GetUnrecordedPastSessionsAsync(today, 200, ct);
        foreach (var session in stale)
        {
            ClearProposal(session);
            session.SessionStatus = PTSessionStatus.AutoCompleted;
            SettleCredits(session.PTPurchase);
            changed++;
        }
        await repo.SaveChangesAsync(ct);
        return Result<int>.Success(changed);
    }, ct);

    public Task<Result<PTSessionResponse>> Note(string actor, int id, PTNoteRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var session = await repo.GetTrainerSessionAsync(id, actor, ct);
        if (session is null)
            return Missing<PTSessionResponse>();
        if (session.SessionStatus is not (PTSessionStatus.Completed or PTSessionStatus.AutoCompleted) ||
            string.IsNullOrWhiteSpace(request.Note))
            return Invalid<PTSessionResponse>("Chỉ ghi chú cho buổi đã hoàn thành.");
        session.Note = request.Note.Trim();
        session.NoteUpdatedAt = Now;
        session.NoteAuthorUserId = actor;
        Audit(actor, nameof(PTSession), session.Id, "CoachingNoteUpdated");
        await repo.SaveChangesAsync(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, Now));
    }, ct);

    /// <summary>UC 4.10 / GBR-108: the completed contract's final session, if it was held; its trainer is the one reviewed.</summary>
    private PTSession? FinalTaughtSession(PTPurchase p)
    {
        if (EffectiveStatus(p) != PTPurchaseStatus.Completed)
            return null;
        var last = p.Sessions.Where(s => s.SessionStatus != PTSessionStatus.MakeUpPending &&
                (s.SessionStatus != PTSessionStatus.Cancelled || s.IsCreditForfeited))
            .OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.StartTime).FirstOrDefault();
        return last?.SessionStatus is PTSessionStatus.Completed or PTSessionStatus.AutoCompleted ? last : null;
    }

    public Task<Result<int>> Review(string actor, int id, PTReviewRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<int>();
        var p = await MemberContract(actor, id, ct);
        if (p is null)
            return Missing<int>();
        var last = FinalTaughtSession(p);
        if (last is null || await repo.HasReviewAsync(id, ct))
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
        repo.Add(review);
        await repo.SaveChangesAsync(ct);
        return Result<int>.Success(review.Id, 201);
    }, ct);
}
