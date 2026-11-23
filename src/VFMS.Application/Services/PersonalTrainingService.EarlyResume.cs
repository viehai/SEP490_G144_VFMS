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
        var result = await PlanEarlyResume(actor, id, request.ResumeDate, request.Moves ?? [], null, ct);
        return result.IsSuccess
            ? Result<PTEarlyResumePreview>.Success(result.Data!.Preview)
            : Result<PTEarlyResumePreview>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
    }

    public Task<Result<PTContractResponse>> ConfirmEarlyResume(
        string actor, int id, PTEarlyResumeConfirmation request, CancellationToken ct) => Write(async () =>
    {
        var result = await PlanEarlyResume(actor, id, request.ResumeDate, request.Moves, request.CurrentResumeDate, ct);
        if (!result.IsSuccess)
            return Result<PTContractResponse>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);

        var plan = result.Data!;
        if (plan.Preview.SessionsNeedingNewSlot.Count != 0)
            return Conflict<PTContractResponse>("Cần chọn ca mới cho các buổi nằm ngoài thời hạn đã điều chỉnh.");

        foreach (var (session, slot) in plan.Replacements)
        {
            session.SessionDate = slot.Date;
            session.StartTime = slot.StartTime;
            session.EndTime = slot.EndTime;
            session.PTAvailabilityId = slot.TemplateId;
        }
        plan.Freeze.ActualEndDate = request.ResumeDate;
        plan.Purchase.EndDate = plan.Preview.ExpiresOn;
        Audit(actor, nameof(PTFreeze), id,
            FormattableString.Invariant($"EarlyResume:{plan.Preview.CurrentResumeDate:yyyy-MM-dd}->{request.ResumeDate:yyyy-MM-dd}"));
        var message = $"Ngày tập lại đã đổi thành {request.ResumeDate:dd/MM/yyyy}. Hãy kiểm tra lịch PT đã cập nhật.";
        Notify(actor, "Tập lại PT sớm", message, nameof(PTFreeze), id);
        Notify(plan.Purchase.PTUserId, "Tập lại PT sớm", message, nameof(PTFreeze), id);
        await repo.SaveChangesAsync(ct);
        return Result<PTContractResponse>.Success(ContractResponse(plan.Purchase));
    }, ct);

    private async Task<Result<EarlyResumePlan>> PlanEarlyResume(string actor, int id, DateOnly resumeDate,
        IReadOnlyList<PTEarlyResumeMove> moves, DateOnly? expectedResumeDate, CancellationToken ct)
    {
        if (await User(actor, UserRole.Member, ct) is null)
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
        if (resumeDate < today || resumeDate >= currentResume)
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
        var templateIds = templates.ToDictionary(t => (t.DayOfWeek!.Value, t.StartTime), t => t.Id);
        var validSlots = new Dictionary<int, PTCalendarSlot>();
        foreach (var move in candidates)
        {
            var end = move.StartTime.AddHours(movable[move.SessionId].CreditUnits);
            if (move.Date >= resumeDate && (scheduledEnd || move.Date <= expiresOn) && Utc(move.Date, move.StartTime) > now &&
                InHours(purchase.PTUser.Gym, move.StartTime, end) &&
                templateIds.TryGetValue((move.Date.DayOfWeek, move.StartTime), out var templateId))
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
        // GBR-10: Gym access is checked when attendance is recorded, not when the freeze ends early.

        if (scheduledEnd)
            expiresOn = ScheduledEnd(purchase, projectedSessions.Select(s => s.Date));
        var preview = new PTEarlyResumePreview(currentResume, resumeDate, expiresOn,
            resumeDate.DayNumber - start.DayNumber, RemainingFreezeDays(purchase),
            replacements.Select(r => new PTEarlyResumeMove(r.Original.Id, r.Slot.Date, r.Slot.StartTime)).ToArray(),
            projectedSessions, needsNewSlot.Order().ToArray(), unavailableOriginals);
        return Result<EarlyResumePlan>.Success(new(purchase, freeze, preview, replacements));
    }
}
