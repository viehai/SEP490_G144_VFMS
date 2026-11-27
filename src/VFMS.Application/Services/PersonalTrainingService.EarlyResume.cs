using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    private sealed record EarlyResumePlan(PTPurchase Purchase, PTFreeze Freeze, PTEarlyResumePreview Preview,
        IReadOnlyList<Replacement> Replacements);

    public async Task<Result<PTEarlyResumePreview>> PreviewEarlyResume(
        string actor, int id, PTEarlyResumeRequest request, CancellationToken ct)
    {
        var result = await PlanEarlyResume(actor, id, request.ResumeDate, request.Moves ?? [], null, ct, request.TemplateIds);
        return result.IsSuccess
            ? Result<PTEarlyResumePreview>.Success(result.Data!.Preview)
            : Result<PTEarlyResumePreview>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
    }

    /// <summary>UC 4.8 step 8.3: the Member confirms the revision; it becomes a request the Manager approves.</summary>
    public Task<Result<PTContractResponse>> ConfirmEarlyResume(
        string actor, int id, PTEarlyResumeConfirmation request, CancellationToken ct) => Write(async () =>
    {
        var result = await PlanEarlyResume(actor, id, request.ResumeDate, request.Moves, request.CurrentResumeDate, ct);
        if (!result.IsSuccess)
            return Result<PTContractResponse>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);

        var plan = result.Data!;
        if (plan.Preview.SessionsNeedingNewSlot.Count != 0)
            return Conflict<PTContractResponse>("Cần chọn ca mới cho các buổi nằm ngoài thời hạn đã điều chỉnh.");
        if (ChangePending(plan.Freeze))
            return Result<PTContractResponse>.Failure("Yêu cầu của bạn đang chờ Quản lý duyệt.", "PT_FREEZE_CHANGE_PENDING", 409);

        // The freeze and the schedule stay in effect until the Manager approves (step 8.4).
        StartChangeRequest(plan.Freeze, PTFreezeChangeState.EarlyResumePending,
            System.Text.Json.JsonSerializer.Serialize(request));
        Audit(actor, nameof(PTFreeze), id,
            FormattableString.Invariant($"EarlyResumeRequested:{plan.Preview.CurrentResumeDate:yyyy-MM-dd}->{request.ResumeDate:yyyy-MM-dd}"));
        Notify(actor, "Đã gửi yêu cầu tập lại sớm",
            $"Bảo lưu vẫn giữ nguyên cho đến khi Quản lý duyệt ngày tập lại {request.ResumeDate:dd/MM/yyyy}.", nameof(PTFreeze), id);
        foreach (var manager in await ManagerIds(plan.Purchase.PTUserId, ct))
            Notify(manager, "Yêu cầu tập lại sớm PT",
                $"Hội viên xin tập lại sớm từ {request.ResumeDate:dd/MM/yyyy}.", nameof(PTFreeze), id);
        await repo.SaveChangesAsync(ct);
        return Result<PTContractResponse>.Success(ContractResponse(plan.Purchase));
    }, ct);

    private async Task<Result<EarlyResumePlan>> PlanEarlyResume(string actor, int id, DateOnly resumeDate,
        IReadOnlyList<PTEarlyResumeMove> moves, DateOnly? expectedResumeDate, CancellationToken ct,
        int[]? templateIds = null, bool atApproval = false)
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<EarlyResumePlan>();
        var purchase = await MemberFreeze(actor, id, ct);
        if (purchase is null)
            return Missing<EarlyResumePlan>();

        var freeze = purchase.PTFreezes.Single(f => f.Id == id);
        var now = Now;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        if (!Approved(freeze) || freeze.ActualStartDate is not { } start || freeze.ActualEndDate is not { } currentResume ||
            start > today || today >= currentResume || EffectiveStatus(purchase) != PTPurchaseStatus.Frozen)
            return Conflict<EarlyResumePlan>("Chỉ có thể tập lại sớm khi gói PT đang bảo lưu.");
        if (expectedResumeDate is { } expected && currentResume != expected)
            return Conflict<EarlyResumePlan>("Ngày tập lại hiện tại đã thay đổi. Vui lòng xem lại lịch dự kiến.");
        // The date rule: today <= new Resume Date < ActualEndDate. It is checked when the Member sends the request; a Manager
        // who approves later is not blocked because that date has meanwhile become today or earlier.
        if ((!atApproval && resumeDate < today) || resumeDate >= currentResume)
            return Invalid<EarlyResumePlan>("Chọn ngày tập lại từ hôm nay và trước ngày đã duyệt.");
        if (moves.Any(m => m is null) || moves.Select(m => m.SessionId).Distinct().Count() != moves.Count)
            return Invalid<EarlyResumePlan>("Mỗi buổi chỉ được chọn một ca thay thế.");

        var removedDays = currentResume.DayNumber - resumeDate.DayNumber;
        var scheduledEnd = UsesScheduledEnd(purchase);
        var expiresOn = scheduledEnd ? purchase.EndDate : purchase.EndDate.AddDays(-removedDays);
        var remaining = purchase.Sessions.Where(s => PersonalTrainingRules.Reserved(s.SessionStatus) &&
            Utc(s.SessionDate, s.StartTime) > now).ToArray();
        var originalSessions = purchase.Sessions
            .Where(s => s.SessionStatus == PTSessionStatus.Cancelled && !s.IsCreditForfeited &&
                s.SessionDate >= resumeDate && s.SessionDate < currentResume)
            .ToDictionary(s => s.Id);
        var needsNewSlot = remaining.Where(s => s.SessionDate < resumeDate || (!scheduledEnd && s.SessionDate > expiresOn))
            .Select(s => s.Id).ToHashSet();
        var movable = remaining.Where(s => s.SessionStatus == PTSessionStatus.Scheduled &&
                (needsNewSlot.Contains(s.Id) || s.ReplacementForSessionId is { } originalId &&
                    originalSessions.ContainsKey(originalId)))
            .ToDictionary(s => s.Id);
        if (moves.Any(m => !movable.ContainsKey(m.SessionId)))
            return Invalid<EarlyResumePlan>("Chỉ được điều chỉnh buổi bị ảnh hưởng bởi việc tập lại sớm.");

        var explicitMoves = moves.Select(m => m.SessionId).ToHashSet();
        var candidates = moves.ToList();
        if (expectedResumeDate is null)
        {
            foreach (var session in movable.Values.OrderBy(s => s.Id))
            {
                if (!explicitMoves.Contains(session.Id) && session.ReplacementForSessionId is { } originalId &&
                    originalSessions.TryGetValue(originalId, out var original))
                    candidates.Add(new(session.Id, original.SessionDate, original.StartTime));
            }
        }

        var templates = await Templates(purchase.PTUserId, ct);
        if (templateIds is { Length: > 0 })
        {
            // The Member chose weekly slots: affected sessions without a hand-picked slot follow them from the new resume date.
            var chosen = templates.Where(t => templateIds.Contains(t.Id)).ToArray();
            if (chosen.Length != templateIds.Distinct().Count() || chosen.Select(t => t.DayOfWeek).Distinct().Count() != chosen.Length)
                return Invalid<EarlyResumePlan>("Chọn một ca đã công bố của huấn luyện viên cho mỗi ngày tập.");
            var recurring = chosen.Select(t => new PTAvailability { DayOfWeek = t.DayOfWeek, StartTime = t.StartTime }).ToArray();
            var days = remaining.Where(s => !movable.ContainsKey(s.Id)).Select(s => s.SessionDate).ToHashSet();
            using var next = PersonalTrainingRules.Occurrences(resumeDate, resumeDate.AddDays(365), recurring, int.MaxValue)
                .Where(o => !days.Contains(o.Item1) && Utc(o.Item1, o.Item2.StartTime) > now).GetEnumerator();
            candidates.RemoveAll(c => !explicitMoves.Contains(c.SessionId));
            foreach (var session in movable.Values.Where(s => !explicitMoves.Contains(s.Id)).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime))
                if (next.MoveNext())
                    candidates.Add(new(session.Id, next.Current.Item1, next.Current.Item2.StartTime));
        }
        var templateIdByStart = templates.ToDictionary(t => (t.DayOfWeek!.Value, t.StartTime), t => t.Id);
        var validSlots = new Dictionary<int, PTCalendarSlot>();
        foreach (var move in candidates)
        {
            var end = move.StartTime.AddHours(movable[move.SessionId].CreditUnits);
            if (move.Date >= resumeDate && (scheduledEnd || move.Date <= expiresOn) && Utc(move.Date, move.StartTime) > now &&
                InHours(purchase.PTUser.Gym, move.StartTime, end) &&
                templateIdByStart.TryGetValue((move.Date.DayOfWeek, move.StartTime), out var templateId))
                validSlots.Add(move.SessionId, new(templateId, move.Date, move.StartTime, end, "Available"));
        }
        var busy = await ConflictingSlots(purchase.PTUserId, actor, validSlots.Values.ToArray(), now, ct,
            ignorePurchase: purchase.Id);
        var occupiedDays = remaining.Where(s => !explicitMoves.Contains(s.Id)).GroupBy(s => s.SessionDate)
            .ToDictionary(group => group.Key, group => group.Count());
        var replacements = new List<Replacement>();
        var unavailableOriginals = new List<int>();
        foreach (var move in candidates)
        {
            var session = movable[move.SessionId];
            var explicitMove = explicitMoves.Contains(session.Id);
            var onSameDay = !explicitMove && session.SessionDate == move.Date;
            var occupied = occupiedDays.GetValueOrDefault(move.Date) > (onSameDay ? 1 : 0);
            if (!validSlots.TryGetValue(session.Id, out var slot) || busy.Contains(slot) || occupied)
            {
                if (explicitMove)
                    return Conflict<EarlyResumePlan>("Khung giờ đã chọn không còn khả dụng.");
                unavailableOriginals.Add(session.Id);
                continue;
            }

            if (!explicitMove)
                occupiedDays[session.SessionDate]--;
            occupiedDays[move.Date] = occupiedDays.GetValueOrDefault(move.Date) + 1;
            needsNewSlot.Remove(session.Id);
            replacements.Add(new(session, slot));
        }

        var moved = replacements.ToDictionary(r => r.Original.Id, r => r.Slot);
        var projectedSessions = remaining.Select(s => moved.TryGetValue(s.Id, out var slot)
                ? SessionResponse(s, now) with { Date = slot.Date, StartTime = slot.StartTime, EndTime = slot.EndTime }
                : SessionResponse(s, now))
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToArray();

        if (scheduledEnd)
            expiresOn = ScheduledEnd(purchase, projectedSessions.Select(s => s.Date));
        var preview = new PTEarlyResumePreview(currentResume, resumeDate, expiresOn,
            resumeDate.DayNumber - start.DayNumber, RemainingFreezeDays(purchase),
            replacements.Select(r => new PTEarlyResumeMove(r.Original.Id, r.Slot.Date, r.Slot.StartTime)).ToArray(),
            projectedSessions, needsNewSlot.Order().ToArray(), unavailableOriginals);
        return Result<EarlyResumePlan>.Success(new(purchase, freeze, preview, replacements));
    }
}
