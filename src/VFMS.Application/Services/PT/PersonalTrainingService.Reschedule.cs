using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class PersonalTrainingService
{
    public async Task<Result<IReadOnlyList<PTRescheduleSlot>>> RescheduleOptions(
        string actor, int id, DateOnly? from, CancellationToken ct)
    {
        var asTrainer = await User(actor, UserRole.PT, ct) is not null;
        if (!asTrainer && await User(actor, UserRole.Member, ct) is null)
            return Forbidden<IReadOnlyList<PTRescheduleSlot>>();
        var contract = await ContractForSession(actor, id, asTrainer, ct);
        if (contract is null)
            return Missing<IReadOnlyList<PTRescheduleSlot>>();
        var session = contract.Sessions.Single(s => s.Id == id);
        var now = Now;
        if (EffectiveStatus(contract) != PTPurchaseStatus.Active || !PersonalTrainingRules.Reserved(session.SessionStatus) ||
            session.SessionStatus == PTSessionStatus.PendingFreeze || Utc(session.SessionDate, session.StartTime) <= now)
            return Conflict<IReadOnlyList<PTRescheduleSlot>>("Chỉ có thể xem lịch đổi ca cho buổi tập sắp tới.");

        var start = from ?? Today;
        if (start < Today || start.DayNumber > DateOnly.MaxValue.DayNumber - 13)
            return Invalid<IReadOnlyList<PTRescheduleSlot>>("Ngày xem lịch không hợp lệ.");
        var through = start.AddDays(13);
        var templates = await Templates(contract.PTUserId).AsNoTracking(store).ToListAsync(store, ct);
        var duration = session.EndTime - session.StartTime;
        var candidates = PersonalTrainingRules.Occurrences(start, through, templates, int.MaxValue)
            .Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.StartTime.Add(duration), "Available"))
            .Where(s => s.EndTime > s.StartTime).ToArray();
        var conflicts = await ConflictingSlots(contract.PTUserId, contract.MemberUserId, candidates, now, ct,
            ignoreSession: id);
        var bookings = await Occupied(now).Where(s => s.PTUserId == contract.PTUserId &&
                s.SessionDate >= start && s.SessionDate <= through)
            .Select(s => new PTRescheduleSlot(s.SessionDate, s.StartTime, s.EndTime, "Busy")).ToListAsync(store, ct);
        bookings.AddRange(await Yoga(contract.PTUserId, null, start, through)
            .Select(s => new PTRescheduleSlot(s.ScheduledDate, s.StartTime, s.EndTime, "Busy")).ToListAsync(store, ct));

        var slots = candidates.Select(slot =>
        {
            var busy = conflicts.Contains(slot) || IsOriginalTime(session, slot.Date, slot.StartTime);
            var status = busy ? "Busy" :
                RescheduleTimeEligible(contract, slot.Date, slot.StartTime, slot.EndTime, now)
                    ? "Available"
                    : "Unavailable";
            return new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, status);
        }).Union(bookings).OrderBy(s => s.Date).ThenBy(s => s.StartTime).ThenBy(s => s.EndTime).ToArray();
        return Result<IReadOnlyList<PTRescheduleSlot>>.Success(slots);
    }

    public Task<Result<PTSessionResponse>> RequestReschedule(
        string actor, int id, PTRescheduleRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.Member, ct) is null)
            return Forbidden<PTSessionResponse>();
        var contract = await ContractForSession(actor, id, false, ct);
        if (contract is null)
            return Missing<PTSessionResponse>();
        var session = contract.Sessions.Single(s => s.Id == id);
        var now = Now;
        var originalStart = Utc(session.SessionDate, session.StartTime);
        if (session.SessionStatus == PTSessionStatus.ReschedulePending && session.ProposalExpiresAt <= now)
            ClearProposal(session);
        if (session.SessionStatus != PTSessionStatus.Scheduled || originalStart <= now)
            return Conflict<PTSessionResponse>("Chỉ có thể xin đổi buổi sắp tới chưa có đề xuất chờ.");
        if (LeadTimeError<PTSessionResponse>(session, now) is { } late)
            return late;
        if (IsOriginalTime(session, request.Date, request.StartTime))
            return Invalid<PTSessionResponse>("Chọn lịch khác với lịch hiện tại.");

        var end = request.StartTime.Add(session.EndTime - session.StartTime);
        if (await FindRescheduleTemplate(contract, session, request.Date, request.StartTime, end, now, ct) is null)
            return Conflict<PTSessionResponse>("Khung giờ đã chọn không khả dụng hoặc không đủ điều kiện đổi lịch.");

        session.ProposedDate = request.Date;
        session.ProposedStartTime = request.StartTime;
        session.ProposedEndTime = end;
        session.ProposedAdjustmentType = PTAdjustmentType.Reschedule;
        session.ProposalExpiresAt = ResponseDeadline(originalStart);
        session.SessionStatus = PTSessionStatus.RescheduleRequested;
        Audit(actor, nameof(PTSession), id, $"RescheduleRequested:{request.Date:yyyy-MM-dd}:{request.StartTime:HH:mm}");
        Notify(session.PTUserId, "Yêu cầu đổi lịch PT",
            $"Hội viên đề nghị đổi sang {request.Date:dd/MM/yyyy} {request.StartTime:HH:mm}–{end:HH:mm}.",
            nameof(PTSession), id);
        await store.Save(ct);
        return Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    public Task<Result<PTSessionResponse>> DecideReschedule(
        string actor, int id, PTRescheduleDecision request, CancellationToken ct) => Write(async () =>
    {
        if (await User(actor, UserRole.PT, ct) is null)
            return Forbidden<PTSessionResponse>();
        var contract = await ContractForSession(actor, id, true, ct);
        if (contract is null)
            return Missing<PTSessionResponse>();
        var session = contract.Sessions.Single(s => s.Id == id);
        if (session.SessionStatus != PTSessionStatus.RescheduleRequested || session.ProposedDate != request.Date ||
            session.ProposedStartTime != request.StartTime || session.ProposalExpiresAt != request.ProposalExpiresAt)
            return Conflict<PTSessionResponse>("Yêu cầu đã thay đổi. Vui lòng tải lại.");

        var now = Now;
        var expired = session.ProposalExpiresAt <= now;
        if (!expired && request.Accept && session.ProposedAdjustmentType == PTAdjustmentType.Cancellation)
        {
            AcceptCancellation(session, contract, actor, now);
            await store.Save(ct);
            return Result<PTSessionResponse>.Success(SessionResponse(session, now));
        }
        var outcome = "RescheduleDeclined";
        var message = "PT đã từ chối đổi lịch. Lịch cũ được giữ nguyên.";
        var unavailable = false;
        if (expired)
        {
            outcome = "RescheduleExpired";
            message = "Yêu cầu đổi lịch đã hết hạn. Lịch cũ được giữ nguyên.";
        }
        else if (request.Accept)
        {
            var end = session.ProposedEndTime!.Value;
            var template = await FindRescheduleTemplate(contract, session, request.Date, request.StartTime, end, now, ct);
            unavailable = template is null;
            if (unavailable)
            {
                outcome = "RescheduleUnavailable";
                message = "Khung giờ đề xuất không còn khả dụng. Lịch cũ được giữ nguyên.";
                Notify(session.PTUserId, "Yêu cầu đổi lịch PT", message, nameof(PTSession), id);
            }
            else
            {
                session.SessionDate = request.Date;
                session.StartTime = request.StartTime;
                session.EndTime = end;
                session.PTAvailabilityId = template;
                outcome = "RescheduleAccepted";
                message = $"PT đã đồng ý đổi lịch sang {request.Date:dd/MM/yyyy} {request.StartTime:HH:mm}–{end:HH:mm}.";
            }
        }

        CloseMemberReschedule(session, actor, outcome, message);
        await store.Save(ct);
        return expired || unavailable
            ? Conflict<PTSessionResponse>(message)
            : Result<PTSessionResponse>.Success(SessionResponse(session, now));
    }, ct);

    private void CloseMemberReschedule(PTSession session, string? actor, string outcome, string message)
    {
        ClearProposal(session);
        Audit(actor, nameof(PTSession), session.Id, outcome);
        Notify(session.MemberUserId, "Yêu cầu đổi lịch PT", message, nameof(PTSession), session.Id);
    }

    private static bool IsOriginalTime(PTSession session, DateOnly date, TimeOnly start) =>
        date == session.SessionDate && start == session.StartTime;

    // GBR-10: Gym access is checked when attendance is recorded, not when a session moves.
    private bool RescheduleTimeEligible(PTPurchase contract, DateOnly date, TimeOnly start, TimeOnly end, DateTime now) =>
        date >= Today && date >= contract.StartDate && date <= contract.EndDate &&
        Utc(date, start) > now && InHours(contract.PTUser.Gym, start, end) &&
        !contract.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
            f.RequestedStartDate <= date && date < (f.ActualEndDate ?? f.RequestedEndDate));

    private async Task<int?> FindRescheduleTemplate(PTPurchase contract, PTSession session, DateOnly date,
        TimeOnly start, TimeOnly end, DateTime now, CancellationToken ct)
    {
        if (EffectiveStatus(contract) != PTPurchaseStatus.Active || date < Today || date > contract.EndDate)
            return null;
        if (!RescheduleTimeEligible(contract, date, start, end, now))
            return null;
        var template = await Templates(contract.PTUserId).Where(t => t.DayOfWeek == date.DayOfWeek && t.StartTime == start)
            .Select(t => (int?)t.Id).FirstOrDefaultAsync(store, ct);
        if (template is null || await HasConflict(contract.PTUserId, contract.MemberUserId,
            [new(template.Value, date, start, end, "Available")], now, ct, ignoreSession: session.Id))
            return null;
        return template;
    }
}
