using System.Text.Json;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    private static bool Approved(PTFreeze f) => f.RequestStatus is PTFreezeRequestStatus.Approved or
        PTFreezeRequestStatus.Active or PTFreezeRequestStatus.Completed;

    /// <summary>GBR-15: one freeze per contract, so nothing remains once a request is pending or the freeze is used.</summary>
    private static int RemainingFreezeDays(PTPurchase p) =>
        p.PTFreezes.Any(f => f.RequestStatus == PTFreezeRequestStatus.Pending || Approved(f)) ? 0 : p.FreezeMaxTotalDaysSnapshot;

    private PTFreezeResponse FreezeResponse(PTFreeze f, PTPurchase p)
    {
        var state = f.RequestStatus;
        if (Approved(f))
            state = Today >= f.ActualEndDate ? PTFreezeRequestStatus.Completed :
                Today >= f.ActualStartDate ? PTFreezeRequestStatus.Active : PTFreezeRequestStatus.Approved;
        return new(f.Id, p.Id, p.MemberUserId, p.PTUserId, f.RequestedStartDate,
            f.ActualEndDate ?? f.RequestedEndDate, state.ToString(), f.Reason,
            f.ReviewNote, f.ReviewedAt, ReplacementTrainerId: f.ReplacementTrainerId)
        {
            // UC 4.9: the freeze policy snapshot the Manager reviews against.
            MinimumDays = f.MinimumDaysSnapshot ?? p.FreezeMinimumDaysSnapshot,
            MaximumDays = f.MaxDaysSnapshot ?? p.FreezeMaxTotalDaysSnapshot,
            ChangeRequestType = f.ChangeRequestType,
            ChangeRequestStatus = f.ChangeRequestStatus,
            ChangeRequestedAt = f.ChangeRequestedAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            ChangeResumeDate = f.ChangeResumeDate,
            ChangeReviewNote = f.ChangeReviewNote,
            ChangeOutcome = f.ChangeOutcome
        };
    }

    public async Task<Result<PagedResponse<PTFreezeResponse>>> FreezePage(string actor, bool managerView, bool processed,
        int page, int? contractId, CancellationToken ct)
    {
        var user = managerView ? await User(actor, UserRole.Manager, ct) : await Purchaser(actor, ct);
        if (user is null)
            return Forbidden<PagedResponse<PTFreezeResponse>>();
        var rows = await repo.GetFreezePageAsync(managerView ? user.GymId : null, managerView ? null : actor,
            contractId, processed, page, PageSize, ct);
        return PageOf(rows, row => FreezeResponse(row.Freeze, row.Contract) with
        {
            MemberName = row.MemberName,
            TrainerName = row.TrainerName,
            // UC 4.9 step 2: the table shows the proposed schedule and the replacement trainer.
            ReplacementTrainerName = row.ReplacementTrainerName,
            ProposedSlots = row.HeldSlots
        });
    }

    // Shared with early resume.
    private sealed record Replacement(PTSession Original, PTCalendarSlot Slot);
    private sealed record SessionBasedPattern(PTSlotInput[] Slots);

    private static bool UsesScheduledEnd(PTPurchase purchase) => purchase.WeeklyPatternJson?.StartsWith('{') == true;

    private static DateOnly ScheduledEnd(PTPurchase purchase, IEnumerable<DateOnly> dates) =>
        dates.DefaultIfEmpty(purchase.StartDate).Max();

    /// <summary>GBR-11: the end date is the last session that is still reserved or already used.</summary>
    private static DateOnly LastSessionDate(PTPurchase p) => ScheduledEnd(p, p.Sessions
        .Where(s => PersonalTrainingRules.Reserved(s.SessionStatus) || PersonalTrainingRules.Used(s.SessionStatus) ||
            s.SessionStatus == PTSessionStatus.FreezeHold)
        .Select(s => s.SessionDate));

    private sealed record FreezePlan(string TrainerId, IReadOnlyList<Replacement> Sessions, IReadOnlyList<int> Missing,
        IReadOnlyList<Replacement> Conflicts, DateOnly End);

    private static bool InFreezeWindow(PTSession s, DateOnly start, DateOnly resume) =>
        s.SessionDate >= start && s.SessionDate < resume &&
        (AwaitingAttendance(s.SessionStatus) || s.SessionStatus == PTSessionStatus.PendingFreeze);

    /// <summary>
    /// UC 4.8 with the same trainer: each frozen session moves to the Member's chosen slot, or to the next free slot of the
    /// current weekly pattern from the Resume Date.
    /// </summary>
    private async Task<Result<FreezePlan>> PlanSameTrainer(PTPurchase p, DateOnly start, DateOnly resume,
        IReadOnlyList<PTEarlyResumeMove> moves, CancellationToken ct, int[]? templateIds = null)
    {
        var affected = p.Sessions.Where(s => InFreezeWindow(s, start, resume))
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).ToArray();
        if (moves.Any(m => affected.All(s => s.Id != m.SessionId)))
            return Invalid<FreezePlan>("Chỉ được chọn ca mới cho các buổi nằm trong thời gian bảo lưu.");
        var templates = await Templates(p.PTUserId, ct);
        PTSlotInput[] pattern;
        if (templateIds is { Length: > 0 })
        {
            // The Member picked the weekly slots after the freeze from the trainer's published schedule.
            var chosen = templates.Where(t => templateIds.Contains(t.Id)).ToArray();
            if (chosen.Length != templateIds.Distinct().Count() || chosen.Select(t => t.DayOfWeek).Distinct().Count() != chosen.Length)
                return Invalid<FreezePlan>("Chọn một ca đã công bố của huấn luyện viên cho mỗi ngày tập.");
            pattern = chosen.Select(t => new PTSlotInput(t.DayOfWeek!.Value, t.StartTime)).ToArray();
        }
        else
            pattern = p.WeeklyPatternJson?.StartsWith('{') == true
                ? JsonSerializer.Deserialize<SessionBasedPattern>(p.WeeklyPatternJson)!.Slots
                : JsonSerializer.Deserialize<PTSlotInput[]>(p.WeeklyPatternJson ?? "[]")!;
        var recurring = pattern.Select(x => new PTAvailability { DayOfWeek = x.DayOfWeek, StartTime = x.StartTime }).ToArray();
        var now = Now;
        var takenDays = p.Sessions.Where(s => !affected.Contains(s) && s.SessionDate >= resume &&
                (PersonalTrainingRules.Reserved(s.SessionStatus) || s.SessionStatus == PTSessionStatus.FreezeHold))
            .Select(s => s.SessionDate).ToHashSet();
        var planned = new List<Replacement>();
        var missing = new List<int>();

        async Task<bool> Fits(PTCalendarSlot slot, bool checkDay) =>
            slot.Date >= resume && Utc(slot.Date, slot.StartTime) > now && InHours(p.PTUser.Gym, slot.StartTime, slot.EndTime) &&
            templates.Any(t => t.DayOfWeek == slot.Date.DayOfWeek && t.StartTime == slot.StartTime) &&
            !(checkDay && takenDays.Contains(slot.Date)) &&
            !await HasConflict(p.PTUserId, p.MemberUserId, [slot], now, ct, ignorePurchase: p.Id);

        // UC 4.8 step 5: the weekly pattern continues from the Resume Date, one pattern slot per frozen session in order.
        // A busy slot is not skipped; that session is left for the Member to choose.
        var defaults = new Dictionary<int, (DateOnly Date, TimeOnly Start)>();
        using (var next = PersonalTrainingRules.Occurrences(resume, resume.AddDays(365), recurring, int.MaxValue)
            .Where(o => !takenDays.Contains(o.Item1) && Utc(o.Item1, o.Item2.StartTime) > now).GetEnumerator())
            foreach (var original in affected)
                if (next.MoveNext())
                    defaults[original.Id] = (next.Current.Item1, next.Current.Item2.StartTime);
        foreach (var (sessionId, slot) in defaults)
            if (moves.All(m => m.SessionId != sessionId))
                takenDays.Add(slot.Date);

        foreach (var original in affected)
        {
            var hours = original.CreditUnits;
            PTCalendarSlot? chosen = null;
            if (moves.FirstOrDefault(m => m.SessionId == original.Id) is { } move)
            {
                var slot = new PTCalendarSlot(0, move.Date, move.StartTime, move.StartTime.AddHours(hours), "Proposed");
                if (!await Fits(slot, checkDay: true))
                    return Conflict<FreezePlan>($"Ca {move.Date:dd/MM/yyyy} {move.StartTime:HH:mm} không còn trống hoặc không hợp lệ.");
                chosen = slot;
            }
            else if (defaults.TryGetValue(original.Id, out var pick))
            {
                var candidate = new PTCalendarSlot(0, pick.Date, pick.Start, pick.Start.AddHours(hours), "Proposed");
                if (await Fits(candidate, checkDay: false))
                    chosen = candidate;
            }
            if (chosen is null) { missing.Add(original.Id); continue; }
            takenDays.Add(chosen.Date);
            planned.Add(new(original, chosen));
        }
        var end = ScheduledEnd(p, planned.Select(x => x.Slot.Date).Concat(p.Sessions.Where(s => !affected.Contains(s) &&
            (PersonalTrainingRules.Reserved(s.SessionStatus) || PersonalTrainingRules.Used(s.SessionStatus))).Select(s => s.SessionDate)));
        return Result<FreezePlan>.Success(new(p.PTUserId, planned, missing, [], end));
    }

    private static PTPlannedSession Planned(Replacement r, string trainerId) =>
        new(r.Original.Id, r.Slot.Date, r.Slot.StartTime, r.Slot.EndTime, trainerId);

    private async Task<Result<FreezePlan>> PlanFreeze(PTPurchase p, PTFreezeRequest input, CancellationToken ct)
    {
        if (input.ReplacementTrainerId is not null)
            return Conflict<FreezePlan>("Không còn hỗ trợ đổi huấn luyện viên của hợp đồng PT.");
        if (EffectiveStatus(p) != PTPurchaseStatus.Active || input.StartDate < Today ||
            input.StartDate > p.EndDate || input.StartDate < p.StartDate)
            return Conflict<FreezePlan>("Ngày bảo lưu hoặc trạng thái hợp đồng không hợp lệ.");
        if (p.PTFreezes.Any(f => f.RequestStatus == PTFreezeRequestStatus.Pending || Approved(f)))
            return Conflict<FreezePlan>("Hợp đồng đã có yêu cầu bảo lưu đang chờ hoặc đã dùng lượt bảo lưu duy nhất.");
        var days = input.ResumeDate.DayNumber - input.StartDate.DayNumber;
        if (days < p.FreezeMinimumDaysSnapshot || days > p.FreezeMaxTotalDaysSnapshot)
            return Result<FreezePlan>.Failure(
                $"Thời gian bảo lưu phải từ {p.FreezeMinimumDaysSnapshot} đến {p.FreezeMaxTotalDaysSnapshot} ngày.",
                "PT_FREEZE_DURATION", 409);
        return await PlanSameTrainer(p, input.StartDate, input.ResumeDate, input.Moves ?? [], ct, input.ReplacementTemplateIds);
    }

    /// <summary>UC 4.8 step 5-6: proposed schedule after the freeze, without holding anything.</summary>
    public async Task<Result<PTFreezeProposal>> PreviewFreezeRequest(string actor, int id, PTFreezeRequest input, CancellationToken ct)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<PTFreezeProposal>();
        var p = await MemberContract(actor, id, ct);
        if (p is null)
            return Missing<PTFreezeProposal>();
        var plan = await PlanFreeze(p, input, ct);
        if (!plan.IsSuccess)
            return Result<PTFreezeProposal>.Failure(plan.ErrorMessage!, plan.ErrorCode!, plan.StatusCode);
        var data = plan.Data!;
        return Result<PTFreezeProposal>.Success(new(data.End,
            data.Sessions.Select(r => Planned(r, data.TrainerId)).ToArray(),
            data.Missing.Concat(data.Conflicts.Select(c => c.Original.Id)).ToArray()));
    }

    public Task<Result<PTFreezeResponse>> RequestFreeze(string actor, int id, PTFreezeRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            if (await Purchaser(actor, ct) is null)
                return Forbidden<PTFreezeResponse>();
            // GBR-09: serialize with other holds on the trainer whose slots are held after the freeze, as checkout does.
            var trainerId = await repo.GetContractTrainerIdAsync(id, actor, ct);
            if (trainerId is null)
                return Missing<PTFreezeResponse>();
            await payments.LockUsersAsync([actor, trainerId], ct);
            var p = await MemberContract(actor, id, ct);
            if (p is null)
                return Missing<PTFreezeResponse>();
            var planResult = await PlanFreeze(p, input, ct);
            if (!planResult.IsSuccess)
                return Result<PTFreezeResponse>.Failure(planResult.ErrorMessage!, planResult.ErrorCode!, planResult.StatusCode);
            var plan = planResult.Data!;
            if (plan.Missing.Count > 0 || plan.Conflicts.Count > 0)
                return Result<PTFreezeResponse>.Failure("Một số buổi chưa có ca trống sau bảo lưu. Hãy chọn ca khác.",
                    "PT_FREEZE_SLOT_REQUIRED", 409);

            var freeze = new PTFreeze
            {
                PTPurchaseId = id,
                RequestedByUserId = actor,
                RequestedStartDate = input.StartDate,
                RequestedEndDate = input.ResumeDate,
                Reason = input.Reason.Trim(),
                MinimumDaysSnapshot = p.FreezeMinimumDaysSnapshot,
                MaxDaysSnapshot = p.FreezeMaxTotalDaysSnapshot
            };
            repo.Add(freeze);
            await repo.SaveChangesAsync(ct);
            // Schedule protection: sessions in the window are never penalised while the request is pending.
            foreach (var session in p.Sessions.Where(s => InFreezeWindow(s, input.StartDate, input.ResumeDate)))
            {
                ClearProposal(session);
                session.SessionStatus = PTSessionStatus.PendingFreeze;
            }
            // GBR-09: the chosen post-freeze slots are held until the Manager decides.
            foreach (var (original, slot) in plan.Sessions)
                p.Sessions.Add(new PTSession
                {
                    PTUserId = plan.TrainerId,
                    MemberUserId = p.MemberUserId,
                    SessionDate = slot.Date,
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    CreditUnits = original.CreditUnits,
                    SessionStatus = PTSessionStatus.FreezeHold,
                    ReplacementForSessionId = original.Id,
                    PTFreezeId = freeze.Id
                });
            await repo.SaveChangesAsync(ct);
            Notify(actor, "Đã gửi yêu cầu bảo lưu PT",
                "Các buổi trong thời gian bảo lưu được bảo vệ và lịch sau bảo lưu được giữ trong lúc chờ duyệt.", nameof(PTFreeze), freeze.Id);
            foreach (var manager in await ManagerIds(p.PTUserId, ct))
                Notify(manager, "Yêu cầu bảo lưu PT mới", $"{p.Member.FullName} xin bảo lưu từ {input.StartDate:dd/MM/yyyy}.",
                    nameof(PTFreeze), freeze.Id);
            Audit(actor, nameof(PTFreeze), freeze.Id, "Requested");
            await repo.SaveChangesAsync(ct);
            return Result<PTFreezeResponse>.Success(FreezeResponse(freeze, p), 201);
        }, ct);

    // The Manager must belong to the gym of the contract's trainer.
    private async Task<PTPurchase?> ManagerFreeze(string actor, int freezeId, CancellationToken ct)
    {
        var manager = await User(actor, UserRole.Manager, ct);
        var purchase = manager is null ? null : await repo.GetContractForFreezeAsync(freezeId, ct);
        return purchase is not null && purchase.PTUser.GymId == manager!.GymId ? purchase : null;
    }

    private static PTSession[] Holds(PTPurchase p) =>
        p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.FreezeHold).OrderBy(s => s.SessionDate).ToArray();

    public async Task<Result<PTFreezePreview>> PreviewFreeze(string actor, int id, CancellationToken ct)
    {
        var purchase = await ManagerFreeze(actor, id, ct);
        if (purchase is null)
            return Missing<PTFreezePreview>();
        var f = purchase.PTFreezes.Single(x => x.Id == id);
        if (f.RequestStatus != PTFreezeRequestStatus.Pending)
            return Conflict<PTFreezePreview>("Yêu cầu đã được xử lý.");
        var holds = Holds(purchase);
        IReadOnlyList<PTCalendarSlot> slots;
        DateOnly end;
        if (holds.Length > 0)
        {
            slots = holds.Select(h => new PTCalendarSlot(h.PTAvailabilityId ?? 0, h.SessionDate, h.StartTime, h.EndTime, "Held")).ToArray();
            end = LastSessionDate(purchase);
        }
        else
        {
            // Requests made before slot holding existed: suggest slots from the current pattern.
            var plan = await PlanSameTrainer(purchase, f.RequestedStartDate, f.RequestedEndDate, [], ct);
            if (!plan.IsSuccess || plan.Data!.Missing.Count > 0)
                return Conflict<PTFreezePreview>("Không đủ lịch thay thế sau bảo lưu.");
            slots = plan.Data.Sessions.Select(r => r.Slot).ToArray();
            end = plan.Data.End;
        }
        var replacementName = f.ReplacementTrainerId is { } trainerId
            ? await repo.GetUserNameAsync(trainerId, ct)
            : null;
        PTCalendarSlot Slot(PTSession s) => new(s.PTAvailabilityId ?? 0, s.SessionDate, s.StartTime, s.EndTime, s.SessionStatus.ToString());
        return Result<PTFreezePreview>.Success(new(end, slots, replacementName)
        {
            Original = purchase.Sessions.Where(s => InFreezeWindow(s, f.RequestedStartDate, f.RequestedEndDate))
                .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Select(Slot).ToArray(),
            Kept = purchase.Sessions.Where(s => PersonalTrainingRules.Reserved(s.SessionStatus) &&
                    s.SessionStatus != PTSessionStatus.PendingFreeze && !InFreezeWindow(s, f.RequestedStartDate, f.RequestedEndDate))
                .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Select(Slot).ToArray()
        });
    }

    /// <summary>UC 4.9: approve (cancel the frozen sessions and confirm held slots) or reject.</summary>
    public Task<Result<PTFreezeResponse>> DecideFreeze(string actor, int id, PTFreezeDecision input, CancellationToken ct) =>
        Write(async () =>
        {
            var purchase = await ManagerFreeze(actor, id, ct);
            if (purchase is null)
                return Missing<PTFreezeResponse>();
            var f = purchase.PTFreezes.Single(x => x.Id == id);
            if (f.RequestStatus != PTFreezeRequestStatus.Pending)
                return Conflict<PTFreezeResponse>("Yêu cầu đã được xử lý.");
            if (input.Approve && (f.ReplacementTrainerId is not null || Holds(purchase).Any(h => h.PTUserId != purchase.PTUserId)))
                return Conflict<PTFreezeResponse>("Yêu cầu cũ có đổi huấn luyện viên không còn được duyệt. Hãy từ chối để hội viên gửi lại bảo lưu với PT hiện tại.");
            if (ChangePending(f))
                return Conflict<PTFreezeResponse>("Hội viên đã gửi yêu cầu rút bảo lưu. Hãy xử lý yêu cầu rút trước.");
            var now = Now;
            var previousTrainer = purchase.PTUserId;
            if (input.Approve)
            {
                var holds = Holds(purchase);
                if (holds.Length == 0)
                {
                    var plan = await PlanSameTrainer(purchase, f.RequestedStartDate, f.RequestedEndDate, [], ct);
                    if (!plan.IsSuccess || plan.Data!.Missing.Count > 0)
                        return Conflict<PTFreezeResponse>("Không đủ lịch thay thế sau bảo lưu.");
                    holds = plan.Data.Sessions.Select(r =>
                    {
                        var hold = new PTSession
                        {
                            PTUserId = purchase.PTUserId,
                            MemberUserId = purchase.MemberUserId,
                            SessionDate = r.Slot.Date,
                            StartTime = r.Slot.StartTime,
                            EndTime = r.Slot.EndTime,
                            CreditUnits = r.Original.CreditUnits,
                            SessionStatus = PTSessionStatus.FreezeHold,
                            ReplacementForSessionId = r.Original.Id,
                            PTFreezeId = f.Id
                        };
                        purchase.Sessions.Add(hold);
                        return hold;
                    }).ToArray();
                }
                else if (await UnavailableHolds(purchase, f, holds, now, ct) is { Count: > 0 } unavailable)
                {
                    // UC 4.9 'Proposed Slot No Longer Available': the request stays Pending and its sessions stay protected.
                    var dates = string.Join(", ", unavailable.Select(h => $"{h.SessionDate:dd/MM/yyyy} {h.StartTime:HH:mm}"));
                    Notify(purchase.MemberUserId, "Lịch sau bảo lưu không còn khả dụng",
                        $"Các ca {dates} không còn dùng được. Hãy chọn ca khác trong yêu cầu bảo lưu đang chờ.", nameof(PTFreeze), id);
                    Audit(actor, nameof(PTFreeze), id, "ApprovalBlocked:SlotUnavailable");
                    await repo.SaveChangesAsync(ct);
                    return Result<PTFreezeResponse>.Failure(
                        $"Không thể duyệt: các ca {dates} không còn khả dụng. Hội viên đã được thông báo để chọn lại.",
                        "PT_FREEZE_SLOT_UNAVAILABLE", 409);
                }
                foreach (var hold in holds)
                {
                    var original = purchase.Sessions.First(s => s.Id == hold.ReplacementForSessionId);
                    if (PersonalTrainingRules.Used(original.SessionStatus) || original.SessionStatus == PTSessionStatus.Disputed)
                    {
                        // Attended while pending: it counts as used and keeps its date (UC 4.8 'Pending Freeze Sessions').
                        purchase.Sessions.Remove(hold);
                        repo.Remove(hold);
                        continue;
                    }
                    if (original.SessionStatus != PTSessionStatus.Cancelled)
                    {
                        original.SessionStatus = PTSessionStatus.Cancelled;
                        original.CancelledAt = now;
                    }
                    hold.SessionStatus = PTSessionStatus.Scheduled;
                    hold.PTAvailabilityId = (await Templates(hold.PTUserId, ct)).Where(t => t.DayOfWeek == hold.SessionDate.DayOfWeek &&
                        t.StartTime == hold.StartTime).Select(t => (int?)t.Id).FirstOrDefault();
                }
                purchase.EndDate = LastSessionDate(purchase);
                f.ActualStartDate = f.RequestedStartDate;
                f.ActualEndDate = f.RequestedEndDate;
                f.RequestStatus = PTFreezeRequestStatus.Approved;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.Reason))
                    return Invalid<PTFreezeResponse>("Cần nhập lý do từ chối.");
                ReleaseFreeze(purchase, f, now);
                f.RequestStatus = PTFreezeRequestStatus.Rejected;
            }
            f.ReviewedByUserId = actor;
            f.ReviewedAt = now;
            f.ReviewNote = input.Reason?.Trim();
            var message = input.Approve ? "Yêu cầu bảo lưu đã được duyệt. Hãy kiểm tra lịch PT đã cập nhật." :
                "Yêu cầu bảo lưu bị từ chối. Lượt bảo lưu chưa bị tính, bạn có thể gửi yêu cầu mới.";
            Notify(purchase.MemberUserId, "Kết quả bảo lưu PT", message, nameof(PTFreeze), id);
            Notify(previousTrainer, "Kết quả bảo lưu PT", message, nameof(PTFreeze), id);
            Audit(actor, nameof(PTFreeze), id, f.RequestStatus.ToString());
            await repo.SaveChangesAsync(ct);
            return Result<PTFreezeResponse>.Success(FreezeResponse(f, purchase));
        }, ct);

    /// <summary>
    /// UC 4.9 step 7: rechecks the held post-freeze slots before approval. A slot fails when its time has passed, it falls
    /// outside opening hours, its trainer is no longer the assigned active PT, or another
    /// booking now overlaps it (the same check used when the slots were planned).
    /// </summary>
    private async Task<List<PTSession>> UnavailableHolds(PTPurchase purchase, PTFreeze freeze, PTSession[] holds,
        DateTime now, CancellationToken ct)
    {
        var unavailable = new List<PTSession>();
        foreach (var hold in holds)
        {
            var original = purchase.Sessions.FirstOrDefault(s => s.Id == hold.ReplacementForSessionId);
            if (original is not null && (PersonalTrainingRules.Used(original.SessionStatus) ||
                    original.SessionStatus == PTSessionStatus.Disputed))
                continue; // attended while pending: the hold is dropped on approval
            var trainer = await User(hold.PTUserId, UserRole.PT, ct);
            var slot = new PTCalendarSlot(0, hold.SessionDate, hold.StartTime, hold.EndTime, "Held");
            if (freeze.ReplacementTrainerId is not null || hold.PTUserId != purchase.PTUserId || trainer is null ||
                Utc(hold.SessionDate, hold.StartTime) <= now ||
                !InHours(trainer.Gym, hold.StartTime, hold.EndTime) ||
                await HasConflict(hold.PTUserId, purchase.MemberUserId, [slot], now, ct, ignorePurchase: purchase.Id))
                unavailable.Add(hold);
        }
        return unavailable;
    }

    /// <summary>Releases held slots; protected sessions return to normal, or become make-up units if their time passed.</summary>
    private void ReleaseFreeze(PTPurchase purchase, PTFreeze f, DateTime now)
    {
        foreach (var hold in Holds(purchase))
        {
            purchase.Sessions.Remove(hold);
            repo.Remove(hold);
        }
        foreach (var session in purchase.Sessions.Where(s => s.SessionStatus == PTSessionStatus.PendingFreeze &&
            s.SessionDate >= f.RequestedStartDate && s.SessionDate < f.RequestedEndDate))
        {
            if (Utc(session.SessionDate, session.StartTime) <= now)
                MarkMakeUp(session, now);
            else
                session.SessionStatus = PTSessionStatus.Scheduled;
        }
    }

    private async Task<PTPurchase?> MemberFreeze(string actor, int freezeId, CancellationToken ct) =>
        await repo.GetContractForFreezeAsync(freezeId, ct) is { } contract && contract.MemberUserId == actor ? contract : null;

    private static PTPlannedSession HeldSession(PTSession hold) =>
        new(hold.ReplacementForSessionId ?? hold.Id, hold.SessionDate, hold.StartTime, hold.EndTime, hold.PTUserId);

    /// <summary>UC 4.8 step 8.4: the schedule held for a pending request; SessionsNeedingSlot lists held slots no longer usable.</summary>
    public async Task<Result<PTFreezeProposal>> FreezeSlots(string actor, int id, CancellationToken ct)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<PTFreezeProposal>();
        var p = await MemberFreeze(actor, id, ct);
        if (p is null)
            return Missing<PTFreezeProposal>();
        var f = p.PTFreezes.Single(x => x.Id == id);
        if (f.RequestStatus != PTFreezeRequestStatus.Pending)
            return Conflict<PTFreezeProposal>("Chỉ đổi ca được khi yêu cầu bảo lưu đang chờ duyệt.");
        var holds = Holds(p);
        var unavailable = await UnavailableHolds(p, f, holds, Now, ct);
        var end = LastSessionDate(p);
        return Result<PTFreezeProposal>.Success(new(end, holds.Select(HeldSession).ToArray(),
            unavailable.Select(h => HeldSession(h).OriginalSessionId).ToArray()));
    }

    /// <summary>
    /// UC 4.8 step 8.4: the Member moves held post-freeze sessions to other published slots. The request stays Pending with
    /// the same dates (even after the Start Date) and its protected sessions stay protected.
    /// </summary>
    public Task<Result<PTFreezeResponse>> ChangeFreezeSlots(string actor, int id, PTFreezeSlotChange input, CancellationToken ct) =>
        Write(async () =>
        {
            if (await Purchaser(actor, ct) is null)
                return Forbidden<PTFreezeResponse>();
            // GBR-09: serialize with other holds on the trainer, as when the request was made.
            var trainerId = await repo.GetFreezeHoldTrainerIdAsync(id, actor, ct);
            if (trainerId is not null)
                await payments.LockUsersAsync([actor, trainerId], ct);
            var p = await MemberFreeze(actor, id, ct);
            if (p is null)
                return Missing<PTFreezeResponse>();
            var f = p.PTFreezes.Single(x => x.Id == id);
            if (f.RequestStatus != PTFreezeRequestStatus.Pending)
                return Conflict<PTFreezeResponse>("Chỉ đổi ca được khi yêu cầu bảo lưu đang chờ duyệt.");
            if (f.ReplacementTrainerId is not null || Holds(p).Any(h => h.PTUserId != p.PTUserId))
                return Conflict<PTFreezeResponse>("Yêu cầu cũ có đổi huấn luyện viên không còn được sửa. Vui lòng rút hoặc nhờ Quản lý từ chối rồi gửi lại.");
            var now = Now;
            var holds = Holds(p);
            var moves = input.Moves.ToDictionary(m => m.SessionId);
            var moved = holds.Where(h => moves.ContainsKey(HeldSession(h).OriginalSessionId)).ToArray();
            if (moved.Length != moves.Count)
                return Invalid<PTFreezeResponse>("Chỉ đổi được ca của các buổi đang giữ chỗ sau bảo lưu.");
            if ((await UnavailableHolds(p, f, holds, now, ct)).Any(h => !moved.Contains(h)))
                return Result<PTFreezeResponse>.Failure("Còn buổi có ca không còn khả dụng. Hãy chọn ca khác cho các buổi đó.",
                    "PT_FREEZE_SLOT_REQUIRED", 409);
            var takenDays = p.Sessions.Where(s => !moved.Contains(s) &&
                    (PersonalTrainingRules.Reserved(s.SessionStatus) || s.SessionStatus == PTSessionStatus.FreezeHold))
                .Select(s => s.SessionDate).ToHashSet();
            foreach (var hold in moved)
            {
                var move = moves[HeldSession(hold).OriginalSessionId];
                var slot = new PTCalendarSlot(0, move.Date, move.StartTime, move.StartTime.AddHours(hold.CreditUnits), "Held");
                var day = move.Date.DayOfWeek;
                var trainer = await User(hold.PTUserId, UserRole.PT, ct);
                if (trainer is null || move.Date < f.RequestedEndDate || Utc(move.Date, move.StartTime) <= now ||
                    !InHours(trainer.Gym, slot.StartTime, slot.EndTime) || !takenDays.Add(move.Date) ||
                    !(await Templates(hold.PTUserId, ct)).Any(t => t.DayOfWeek == day && t.StartTime == move.StartTime) ||
                    await HasConflict(hold.PTUserId, p.MemberUserId, [slot], now, ct, ignorePurchase: p.Id))
                    return Conflict<PTFreezeResponse>($"Ca {move.Date:dd/MM/yyyy} {move.StartTime:HH:mm} không còn trống hoặc không hợp lệ.");
                hold.SessionDate = slot.Date;
                hold.StartTime = slot.StartTime;
                hold.EndTime = slot.EndTime;
            }
            foreach (var manager in await ManagerIds(p.PTUserId, ct))
                Notify(manager, "Lịch sau bảo lưu PT đã đổi", $"{p.Member.FullName} đã chọn ca mới cho yêu cầu bảo lưu.",
                    nameof(PTFreeze), id);
            Audit(actor, nameof(PTFreeze), id, "SlotsChanged");
            await repo.SaveChangesAsync(ct);
            return Result<PTFreezeResponse>.Success(FreezeResponse(f, p));
        }, ct);

    /// <summary>UC 4.9: Managers get an in-app reminder 24 hours after a request and daily once it is 48 hours old.</summary>
    public Task<Result<int>> RemindPendingFreezes(CancellationToken ct) => Write(async () =>
    {
        var now = Now;
        var due = await repo.GetFreezesDueForReminderAsync(now, 100, ct);
        foreach (var freeze in due)
        {
            freeze.ManagerRemindedAt = now;
            foreach (var manager in await ManagerIds(freeze.PTPurchase.PTUserId, ct))
                Notify(manager, "Nhắc duyệt bảo lưu PT", "Có yêu cầu bảo lưu PT chưa được xử lý.", nameof(PTFreeze), freeze.Id);
        }
        await repo.SaveChangesAsync(ct);
        return Result<int>.Success(due.Count);
    }, ct);
}
