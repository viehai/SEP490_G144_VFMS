using System.Text.Json;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// Withdrawing a freeze and resuming early (UC 4.8 steps 8.2 and 8.3) are requests the Manager decides, not immediate
/// actions. The Member sees what would happen before sending; the freeze stays as it is until the Manager approves.
/// </summary>
public sealed partial class PersonalTrainingService
{
    private static bool ChangePending(PTFreeze f) => f.ChangeRequestStatus == PTFreezeChangeStatuses.Pending;

    private static string ChangeName(string? type) =>
        type == PTFreezeChangeTypes.Withdraw ? "rút yêu cầu bảo lưu" : "tập lại sớm";

    /// <summary>Null when the freeze can be withdrawn: Pending or Approved, before it starts, and without a trainer change.</summary>
    private string? WithdrawBlockMessage(PTFreeze f)
    {
        if (f.RequestStatus == PTFreezeRequestStatus.Pending && f.RequestedStartDate > Today)
            return null;
        if (f.RequestStatus == PTFreezeRequestStatus.Approved && f.ActualStartDate > Today)
            return f.ReplacementTrainerId is not null
                ? "Bảo lưu đã kèm đổi huấn luyện viên nên không thể rút. Vui lòng liên hệ quầy lễ tân."
                : null;
        return "Chỉ rút được yêu cầu đang chờ hoặc đã duyệt trước ngày bắt đầu bảo lưu.";
    }

    /// <summary>
    /// What happens to each frozen session if the freeze is withdrawn: an original slot that is still free is restored and
    /// the session moved by the freeze is removed; otherwise the moved session stays and the reason is reported.
    /// With <paramref name="apply"/> the plan is carried out; without it nothing changes.
    /// </summary>
    private async Task<(List<PTWithdrawItem> Items, List<PTCalendarSlot> Schedule)> PlanWithdraw(
        PTPurchase p, PTFreeze f, DateTime now, bool apply, CancellationToken ct)
    {
        var items = new List<PTWithdrawItem>();
        var schedule = new List<PTCalendarSlot>();
        var restoredReplacements = new HashSet<int>();
        var restoredOriginals = new List<PTSession>();
        if (f.RequestStatus == PTFreezeRequestStatus.Approved)
        {
            foreach (var replacement in p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.Scheduled &&
                s.PTFreezeId == f.Id && s.ReplacementForSessionId != null).ToArray())
            {
                var original = p.Sessions.FirstOrDefault(s => s.Id == replacement.ReplacementForSessionId);
                if (original is null)
                    continue;
                var reason =
                    original.SessionStatus != PTSessionStatus.Cancelled ? "buổi gốc không còn ở trạng thái đã hủy" :
                    original.SessionDate < f.RequestedStartDate || original.SessionDate >= f.RequestedEndDate
                        ? "buổi gốc nằm ngoài thời gian bảo lưu" :
                    Utc(original.SessionDate, original.StartTime) <= now ? "giờ của buổi gốc đã qua" :
                    await HasConflict(p.PTUserId, p.MemberUserId,
                        [new(0, original.SessionDate, original.StartTime, original.EndTime, "Available")], now, ct,
                        ignoreSession: replacement.Id) ? "ca gốc đã có người đặt hoặc trùng lịch" : null;
                items.Add(new(original.SessionDate, original.StartTime, replacement.SessionDate, replacement.StartTime,
                    reason is null, reason));
                if (reason is not null)
                    continue;
                restoredReplacements.Add(replacement.Id);
                restoredOriginals.Add(original);
                if (apply)
                {
                    original.SessionStatus = PTSessionStatus.Scheduled;
                    original.CancelledAt = null;
                    p.Sessions.Remove(replacement);
                    repo.Remove(replacement);
                }
            }
        }
        else if (apply)
            ReleaseFreeze(p, f, now);

        if (apply)
        {
            if (f.RequestStatus == PTFreezeRequestStatus.Approved)
            {
                p.EndDate = LastSessionDate(p);
                f.ActualStartDate = null;
                f.ActualEndDate = null;
            }
            return (items, schedule);
        }

        // Projected schedule after the withdrawal, for the Member and the Manager to review.
        PTCalendarSlot Slot(PTSession s, string status) => new(s.PTAvailabilityId ?? 0, s.SessionDate, s.StartTime, s.EndTime, status);
        foreach (var s in p.Sessions.Where(s => PersonalTrainingRules.Reserved(s.SessionStatus) && !restoredReplacements.Contains(s.Id)))
        {
            if (s.SessionStatus == PTSessionStatus.PendingFreeze)
            {
                // A protected session returns to normal when its time is still ahead (otherwise it becomes a make-up unit).
                if (Utc(s.SessionDate, s.StartTime) > now)
                    schedule.Add(Slot(s, "Restored"));
                continue;
            }
            schedule.Add(Slot(s, s.PTFreezeId == f.Id && s.ReplacementForSessionId != null ? "Kept" : "Scheduled"));
        }
        schedule.AddRange(restoredOriginals.Select(o => Slot(o, "Restored")));
        return (items, schedule.OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToList());
    }

    private static string WithdrawOutcome(IReadOnlyList<PTWithdrawItem> items)
    {
        if (items.Count == 0)
            return "Các buổi đang chờ bảo lưu đã trở lại lịch bình thường và các ca giữ chỗ đã được giải phóng.";
        var restored = items.Count(i => i.Restored);
        var text = $"Đã khôi phục {restored}/{items.Count} buổi về ca gốc.";
        var kept = items.Where(i => !i.Restored).ToArray();
        return kept.Length == 0
            ? text
            : text + " Không khôi phục được " + kept.Length + " buổi, các buổi này giữ ở lịch mới: " +
              string.Join("; ", kept.Select(i =>
                  $"{i.OriginalDate:dd/MM} {i.OriginalStart:HH:mm} giữ ở {i.CurrentDate:dd/MM} {i.CurrentStart:HH:mm} ({i.Reason})")) + ".";
    }

    private static DateOnly ProjectedEnd(PTPurchase p, IEnumerable<PTCalendarSlot> schedule) =>
        ScheduledEnd(p, schedule.Select(s => s.Date));

    /// <summary>
    /// UC 4.8 steps 5-7 and 8.3: the trainer's weekly table (four weeks from the week of <c>From</c>) with free slots, busy slots
    /// and slots that cannot be used, so a session without a slot is picked from the table instead of typed in.
    /// </summary>
    public async Task<Result<IReadOnlyList<PTRescheduleSlot>>> FreezeSlotOptions(
        string actor, int contractId, PTFreezeSlotOptionsRequest input, CancellationToken ct)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<IReadOnlyList<PTRescheduleSlot>>();
        var p = await MemberContract(actor, contractId, ct);
        if (p is null)
            return Missing<IReadOnlyList<PTRescheduleSlot>>();
        var trainerId = input.TrainerId ?? p.PTUserId;
        if (trainerId != p.PTUserId && !await ReplacementAllowed(p, trainerId, ct))
            return Conflict<IReadOnlyList<PTRescheduleSlot>>("Huấn luyện viên thay thế phải cùng cấp với gói và đang nhận học viên mới.");
        var trainer = await User(trainerId, UserRole.PT, ct);
        if (trainer is null)
            return Missing<IReadOnlyList<PTRescheduleSlot>>();

        var now = Now;
        var from = input.From < Today ? Today : input.From;
        var start = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        var through = start.AddDays(27);
        var candidates = PersonalTrainingRules.Occurrences(start, through, await Templates(trainerId, ct), int.MaxValue)
            .Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.StartTime.AddHours(1), "Available"))
            .Where(s => s.EndTime > s.StartTime).ToArray();
        var busy = await ConflictingSlots(trainerId, p.MemberUserId, candidates, now, ct, ignorePurchase: p.Id);
        var occupying = await repo.GetOccupyingSessionsAsync(now, start, through, trainerId, p.MemberUserId, null, p.Id, ct);
        var yoga = await repo.GetYogaSessionsAsync(trainerId, p.MemberUserId, start, through, ct);
        // At most one PT session of the contract on a day.
        var contractDays = p.Sessions.Where(s => s.SessionDate >= from &&
            (PersonalTrainingRules.Reserved(s.SessionStatus) || s.SessionStatus == PTSessionStatus.FreezeHold))
            .Select(s => s.SessionDate).ToHashSet();

        string BusyReason(PTCalendarSlot slot) =>
            occupying.FirstOrDefault(s => s.SessionDate == slot.Date &&
                PersonalTrainingRules.Overlaps(slot.StartTime, slot.EndTime, s.StartTime, s.EndTime)) is { } session
                ? session.PTUserId != trainerId ? "Bạn tập với PT khác" :
                  session.MemberUserId == p.MemberUserId ? "Bạn đã có buổi tập khác" : "PT đã có lịch dạy"
                : yoga.Any(s => s.ScheduledDate == slot.Date &&
                    PersonalTrainingRules.Overlaps(slot.StartTime, slot.EndTime, s.StartTime, s.EndTime)) ? "Lớp Yoga" : "Bận";

        var slots = candidates.Select(slot =>
        {
            string? reason = slot.Date < from ? "Trước ngày tập lại" :
                Utc(slot.Date, slot.StartTime) <= now ? "Giờ đã qua" :
                !InHours(trainer.Gym, slot.StartTime, slot.EndTime) ? "Ngoài giờ mở cửa" : null;
            if (reason is not null)
                return new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, "Unavailable", reason);
            if (busy.Contains(slot))
                return new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, "Busy", BusyReason(slot));
            return contractDays.Contains(slot.Date)
                ? new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, "Unavailable", "Đã có buổi tập khác trong ngày")
                : new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, "Available");
        }).OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToArray();
        return Result<IReadOnlyList<PTRescheduleSlot>>.Success(slots);
    }

    /// <summary>UC 4.8 step 8.2: the Member sees which sessions go back and which cannot, and the resulting calendar.</summary>
    public async Task<Result<PTWithdrawPreview>> PreviewWithdraw(string actor, int id, CancellationToken ct)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<PTWithdrawPreview>();
        var p = await MemberFreeze(actor, id, ct);
        if (p is null)
            return Missing<PTWithdrawPreview>();
        var f = p.PTFreezes.Single(x => x.Id == id);
        if (WithdrawBlockMessage(f) is { } blocked)
            return Conflict<PTWithdrawPreview>(blocked);
        return Result<PTWithdrawPreview>.Success(await WithdrawPreview(p, f, ct));
    }

    private async Task<PTWithdrawPreview> WithdrawPreview(PTPurchase p, PTFreeze f, CancellationToken ct)
    {
        var (items, schedule) = await PlanWithdraw(p, f, Now, apply: false, ct);
        return new(ProjectedEnd(p, schedule), items, schedule);
    }

    /// <summary>UC 4.8 step 8.2: sends the request; the freeze stays in force until the Manager approves.</summary>
    public Task<Result<PTFreezeResponse>> WithdrawFreeze(string actor, int id, CancellationToken ct) => Write(async () =>
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<PTFreezeResponse>();
        var p = await MemberFreeze(actor, id, ct);
        if (p is null)
            return Missing<PTFreezeResponse>();
        var f = p.PTFreezes.Single(x => x.Id == id);
        if (WithdrawBlockMessage(f) is { } blocked)
            return Conflict<PTFreezeResponse>(blocked);
        if (ChangePending(f))
            return Result<PTFreezeResponse>.Failure("Yêu cầu của bạn đang chờ Quản lý duyệt.", "PT_FREEZE_CHANGE_PENDING", 409);
        StartChangeRequest(f, PTFreezeChangeTypes.Withdraw, null, null);
        Audit(actor, nameof(PTFreeze), id, "WithdrawRequested");
        Notify(actor, "Đã gửi yêu cầu rút bảo lưu", "Bảo lưu vẫn giữ nguyên cho đến khi Quản lý duyệt yêu cầu rút.", nameof(PTFreeze), id);
        foreach (var manager in await ManagerIds(p.PTUserId, ct))
            Notify(manager, "Yêu cầu rút bảo lưu PT", $"{p.Member.FullName} xin rút yêu cầu bảo lưu từ {f.RequestedStartDate:dd/MM/yyyy}.",
                nameof(PTFreeze), id);
        await repo.SaveChangesAsync(ct);
        return Result<PTFreezeResponse>.Success(FreezeResponse(f, p));
    }, ct);

    private void StartChangeRequest(PTFreeze f, string type, DateOnly? resumeDate, string? payload)
    {
        f.ChangeRequestType = type;
        f.ChangeRequestStatus = PTFreezeChangeStatuses.Pending;
        f.ChangeRequestedAt = Now;
        f.ChangeResumeDate = resumeDate;
        f.ChangePayloadJson = payload;
        f.ChangeReviewedByUserId = null;
        f.ChangeReviewedAt = null;
        f.ChangeReviewNote = null;
        f.ChangeOutcome = null;
    }

    /// <summary>UC 4.9 (Manager): what the pending request would do, shown before the Manager decides.</summary>
    public async Task<Result<PTFreezeChangePreview>> PreviewFreezeChange(string actor, int id, CancellationToken ct)
    {
        var p = await ManagerFreeze(actor, id, ct);
        if (p is null)
            return Missing<PTFreezeChangePreview>();
        var f = p.PTFreezes.Single(x => x.Id == id);
        if (!ChangePending(f))
            return Conflict<PTFreezeChangePreview>("Không có yêu cầu nào đang chờ duyệt.");
        if (f.ChangeRequestType == PTFreezeChangeTypes.Withdraw)
        {
            if (WithdrawBlockMessage(f) is { } blocked)
                return Conflict<PTFreezeChangePreview>(blocked + " Hãy từ chối yêu cầu này.");
            var withdraw = await WithdrawPreview(p, f, ct);
            return Result<PTFreezeChangePreview>.Success(new(PTFreezeChangeTypes.Withdraw, withdraw.ExpiresOn, withdraw, null));
        }
        var payload = JsonSerializer.Deserialize<PTEarlyResumeConfirmation>(f.ChangePayloadJson ?? "")!;
        var plan = await PlanEarlyResume(p.MemberUserId, id, payload.ResumeDate, payload.Moves, payload.CurrentResumeDate, ct,
            atApproval: true);
        return plan.IsSuccess
            ? Result<PTFreezeChangePreview>.Success(new(PTFreezeChangeTypes.EarlyResume, plan.Data!.Preview.ExpiresOn, null, plan.Data.Preview))
            : Conflict<PTFreezeChangePreview>(plan.ErrorMessage + " Hãy từ chối yêu cầu này.");
    }

    /// <summary>Applies an early-resume plan: sessions move, the new Resume Date is saved and the Member and PT are told.</summary>
    private void ApplyEarlyResume(EarlyResumePlan plan, DateOnly resumeDate, string actor, int id)
    {
        foreach (var (session, slot) in plan.Replacements)
        {
            session.SessionDate = slot.Date;
            session.StartTime = slot.StartTime;
            session.EndTime = slot.EndTime;
            session.PTAvailabilityId = slot.TemplateId;
        }
        plan.Freeze.ActualEndDate = resumeDate;
        plan.Purchase.EndDate = plan.Preview.ExpiresOn;
        Audit(actor, nameof(PTFreeze), id,
            FormattableString.Invariant($"EarlyResume:{plan.Preview.CurrentResumeDate:yyyy-MM-dd}->{resumeDate:yyyy-MM-dd}"));
        var message = $"Ngày tập lại đã đổi thành {resumeDate:dd/MM/yyyy}. Hãy kiểm tra lịch PT đã cập nhật.";
        Notify(plan.Purchase.MemberUserId, "Tập lại PT sớm", message, nameof(PTFreeze), id);
        Notify(plan.Purchase.PTUserId, "Tập lại PT sớm", message, nameof(PTFreeze), id);
    }

    /// <summary>UC 4.9 (Manager): approve or reject a withdraw or early-resume request; the Member is told what happened.</summary>
    public Task<Result<PTFreezeResponse>> DecideFreezeChange(string actor, int id, PTFreezeDecision input, CancellationToken ct) =>
        Write(async () =>
        {
            var p = await ManagerFreeze(actor, id, ct);
            if (p is null)
                return Missing<PTFreezeResponse>();
            var f = p.PTFreezes.Single(x => x.Id == id);
            if (!ChangePending(f))
                return Conflict<PTFreezeResponse>("Không có yêu cầu nào đang chờ duyệt.");
            var name = ChangeName(f.ChangeRequestType);
            var now = Now;
            if (!input.Approve)
            {
                if (string.IsNullOrWhiteSpace(input.Reason))
                    return Invalid<PTFreezeResponse>("Nhập lý do từ chối.");
                f.ChangeRequestStatus = PTFreezeChangeStatuses.Rejected;
                f.ChangeOutcome = "Bảo lưu giữ nguyên như đã duyệt.";
                Notify(p.MemberUserId, $"Yêu cầu {name} bị từ chối",
                    $"Quản lý từ chối: {input.Reason.Trim()}. Lịch bảo lưu hiện tại giữ nguyên.", nameof(PTFreeze), id);
            }
            else if (f.ChangeRequestType == PTFreezeChangeTypes.Withdraw)
            {
                if (WithdrawBlockMessage(f) is { } blocked)
                    return Conflict<PTFreezeResponse>(blocked + " Hãy từ chối yêu cầu này.");
                var (items, _) = await PlanWithdraw(p, f, now, apply: true, ct);
                f.RequestStatus = PTFreezeRequestStatus.Withdrawn;
                f.ChangeOutcome = WithdrawOutcome(items);
                Notify(p.MemberUserId, "Yêu cầu rút bảo lưu đã được duyệt", f.ChangeOutcome, nameof(PTFreeze), id);
                Notify(p.PTUserId, "Hội viên rút yêu cầu bảo lưu", "Lịch tập được khôi phục theo khả năng còn trống.", nameof(PTFreeze), id);
            }
            else
            {
                var payload = JsonSerializer.Deserialize<PTEarlyResumeConfirmation>(f.ChangePayloadJson ?? "")!;
                var result = await PlanEarlyResume(p.MemberUserId, id, payload.ResumeDate, payload.Moves, payload.CurrentResumeDate, ct,
                    atApproval: true);
                if (!result.IsSuccess)
                    return Conflict<PTFreezeResponse>(result.ErrorMessage + " Hãy từ chối yêu cầu này.");
                if (result.Data!.Preview.SessionsNeedingNewSlot.Count != 0)
                    return Conflict<PTFreezeResponse>("Cần chọn ca mới cho các buổi nằm ngoài thời hạn đã điều chỉnh. Hãy từ chối yêu cầu này.");
                ApplyEarlyResume(result.Data, payload.ResumeDate, actor, id);
                f.ChangeOutcome = $"Ngày tập lại mới {payload.ResumeDate:dd/MM/yyyy}, hạn gói {result.Data.Preview.ExpiresOn:dd/MM/yyyy}, " +
                    $"{result.Data.Replacements.Count} buổi được xếp lại.";
            }
            if (input.Approve)
                f.ChangeRequestStatus = PTFreezeChangeStatuses.Approved;
            f.ChangeReviewedByUserId = actor;
            f.ChangeReviewedAt = now;
            f.ChangeReviewNote = input.Approve ? null : input.Reason!.Trim();
            Audit(actor, nameof(PTFreeze), id, (input.Approve ? "ChangeApproved:" : "ChangeRejected:") + f.ChangeRequestType);
            await repo.SaveChangesAsync(ct);
            return Result<PTFreezeResponse>.Success(FreezeResponse(f, p));
        }, ct);
}
