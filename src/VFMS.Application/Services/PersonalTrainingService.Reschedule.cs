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
        if (!asTrainer && await Purchaser(actor, ct) is null)
            return Forbidden<IReadOnlyList<PTRescheduleSlot>>();
        var contract = await ContractForSession(actor, id, asTrainer, ct);
        if (contract is null)
            return Missing<IReadOnlyList<PTRescheduleSlot>>();
        var session = contract.Sessions.Single(s => s.Id == id);
        var now = Now;
        if (EffectiveStatus(contract) != PTPurchaseStatus.Active || !PersonalTrainingRules.Reserved(session.SessionStatus) ||
            session.SessionStatus == PTSessionStatus.PendingFreeze || Utc(session.SessionDate, session.StartTime) <= now)
            return Conflict<IReadOnlyList<PTRescheduleSlot>>("Chỉ có thể xem lịch đổi ca cho buổi tập sắp tới.");

        // 'from' is kept for API compatibility; the window is always the session's week and the next one.
        var (start, through) = RescheduleWindow(session);
        var duration = session.EndTime - session.StartTime;
        // UC 4.7: the Member and the PT both pick the trainer's published slots; the PT also sees the Member's busy times.
        var candidates = PersonalTrainingRules.Occurrences(start, through,
                await Templates(contract.PTUserId, ct), int.MaxValue)
            .Select(x => new PTCalendarSlot(x.Slot.Id, x.Date, x.Slot.StartTime, x.Slot.StartTime.Add(duration), "Available"))
            .Where(s => s.EndTime > s.StartTime).ToArray();
        var conflicts = await ConflictingSlots(contract.PTUserId, contract.MemberUserId, candidates, now, ct,
            ignoreSession: id);
        var occupying = await repo.GetOccupyingSessionsAsync(now, start, through, contract.PTUserId,
            asTrainer ? contract.MemberUserId : null, cancellationToken: ct);
        var yoga = await repo.GetYogaSessionsAsync(contract.PTUserId, asTrainer ? contract.MemberUserId : null,
            start, through, ct);
        // The PT may see the names of the Members they teach; a Member never learns who else the PT teaches.
        var names = new Dictionary<string, string>();
        if (asTrainer)
            foreach (var memberId in occupying.Where(s => s.PTUserId == contract.PTUserId && s.MemberUserId != contract.MemberUserId)
                .Select(s => s.MemberUserId).Distinct())
                names[memberId] = await repo.GetUserNameAsync(memberId, ct) ?? "hội viên";
        string BusyReason(PTSession s) =>
            s.Id == id ? "Lịch hiện tại của buổi này" :
            s.PTUserId != contract.PTUserId ? (asTrainer ? "Hội viên tập với PT khác" : "Bạn tập với PT khác") :
            s.MemberUserId == contract.MemberUserId ? (asTrainer ? "Hội viên đã có buổi tập khác" : "Bạn đã có buổi tập khác") :
            asTrainer ? $"Bạn dạy {names[s.MemberUserId]}" : "PT đã có lịch dạy";
        var bookings = occupying.Select(s => new PTRescheduleSlot(s.SessionDate, s.StartTime, s.EndTime, "Busy", BusyReason(s))).ToList();
        bookings.AddRange(yoga.Select(s => new PTRescheduleSlot(s.ScheduledDate, s.StartTime, s.EndTime, "Busy",
            asTrainer ? "Lớp Yoga" : "PT dạy lớp Yoga")));

        var slots = candidates.Select(slot =>
        {
            var busy = conflicts.Contains(slot) || IsOriginalTime(session, slot.Date, slot.StartTime);
            if (busy)
                return new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, "Busy", bookings.FirstOrDefault(b =>
                    b.Date == slot.Date && PersonalTrainingRules.Overlaps(b.StartTime, b.EndTime, slot.StartTime, slot.EndTime))?.Reason ?? "Bận");
            var blocked = RescheduleBlockReason(contract, slot.Date, slot.StartTime, slot.EndTime, now);
            return new PTRescheduleSlot(slot.Date, slot.StartTime, slot.EndTime, blocked is null ? "Available" : "Unavailable", blocked);
        }).Union(bookings).OrderBy(s => s.Date).ThenBy(s => s.StartTime).ThenBy(s => s.EndTime).ToArray();
        return Result<IReadOnlyList<PTRescheduleSlot>>.Success(slots);
    }

    /// <summary>A session can move only within its own week (Monday to Sunday) or the week after.</summary>
    private static (DateOnly Start, DateOnly End) RescheduleWindow(PTSession session)
    {
        var monday = session.SessionDate.AddDays(-(((int)session.SessionDate.DayOfWeek + 6) % 7));
        return (monday, monday.AddDays(13));
    }

    private static bool InRescheduleWindow(PTSession session, DateOnly date) =>
        RescheduleWindow(session) is var (start, end) && date >= start && date <= end;

    public Task<Result<PTSessionResponse>> RequestReschedule(
        string actor, int id, PTRescheduleRequest request, CancellationToken ct) => Write(async () =>
    {
        if (await Purchaser(actor, ct) is null)
            return Forbidden<PTSessionResponse>();
        var contract = await ContractForSession(actor, id, false, ct);
        if (contract is null)
            return Missing<PTSessionResponse>();
        var session = contract.Sessions.Single(s => s.Id == id);
        var now = Now;
        var originalStart = Utc(session.SessionDate, session.StartTime);
        if (PersonalTrainingRules.PublicSessionStatus(session.SessionStatus) == PTSessionStatus.ReschedulePending &&
            session.ProposalExpiresAt <= now)
            ClearProposal(session);
        if (session.SessionStatus != PTSessionStatus.Scheduled || originalStart <= now)
            return Conflict<PTSessionResponse>("Chỉ có thể xin đổi buổi sắp tới chưa có đề xuất chờ.");
        if (LeadTimeError<PTSessionResponse>(session, now) is { } late)
            return late;
        if (IsOriginalTime(session, request.Date, request.StartTime))
            return Invalid<PTSessionResponse>("Chọn lịch khác với lịch hiện tại.");
        if (!InRescheduleWindow(session, request.Date))
            return Invalid<PTSessionResponse>("Chỉ được đổi sang ca trong tuần của buổi tập hoặc tuần kế tiếp.");

        var end = request.StartTime.Add(session.EndTime - session.StartTime);
        if (await FindRescheduleTemplate(contract, session, request.Date, request.StartTime, end, now, ct) is null)
            return await ScheduleConflict<PTSessionResponse>(contract, id, request.Date, request.StartTime, end, now, ct) ??
                Conflict<PTSessionResponse>("Khung giờ đã chọn không khả dụng hoặc không đủ điều kiện đổi lịch.");

        session.ProposedDate = request.Date;
        session.ProposedStartTime = request.StartTime;
        session.ProposalExpiresAt = ResponseDeadline(originalStart);
        session.SessionStatus = PTSessionStatus.RescheduleRequested;
        Audit(actor, nameof(PTSession), id, $"RescheduleRequested:{request.Date:yyyy-MM-dd}:{request.StartTime:HH:mm}");
        Notify(session.PTUserId, "Yêu cầu đổi lịch PT",
            $"Hội viên đề nghị đổi sang {request.Date:dd/MM/yyyy} {request.StartTime:HH:mm}–{end:HH:mm}.",
            nameof(PTSession), id);
        await repo.SaveChangesAsync(ct);
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
        if (PersonalTrainingRules.PublicSessionStatus(session.SessionStatus) != PTSessionStatus.RescheduleRequested ||
            session.ProposedDate != request.Date ||
            session.ProposedStartTime != request.StartTime || session.ProposalExpiresAt != request.ProposalExpiresAt)
            return Conflict<PTSessionResponse>("Yêu cầu đã thay đổi. Vui lòng tải lại.");

        var now = Now;
        var expired = session.ProposalExpiresAt <= now;
        if (!expired && request.Accept && session.SessionStatus == PTSessionStatus.CancellationRequested)
        {
            AcceptCancellation(session, contract, actor, now);
            await repo.SaveChangesAsync(ct);
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
        await repo.SaveChangesAsync(ct);
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

    private bool RescheduleTimeEligible(PTPurchase contract, DateOnly date, TimeOnly start, TimeOnly end, DateTime now) =>
        RescheduleBlockReason(contract, date, start, end, now) is null;

    /// <summary>Why a published slot cannot take the moved session, or null when it can.</summary>
    private string? RescheduleBlockReason(PTPurchase contract, DateOnly date, TimeOnly start, TimeOnly end, DateTime now) =>
        date < contract.StartDate || date > contract.EndDate
            ? $"Ngoài thời hạn hợp đồng ({contract.StartDate:dd/MM}–{contract.EndDate:dd/MM})"
        : date < Today || Utc(date, start) <= now ? "Đã qua giờ"
        : !InHours(contract.PTUser.Gym, start, end) ? "Ngoài giờ mở cửa"
        : contract.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
            f.RequestedStartDate <= date && date < (f.ActualEndDate ?? f.RequestedEndDate)) ? "Trong thời gian bảo lưu"
        : null;

    private async Task<int?> FindRescheduleTemplate(PTPurchase contract, PTSession session, DateOnly date,
        TimeOnly start, TimeOnly end, DateTime now, CancellationToken ct)
    {
        if (EffectiveStatus(contract) != PTPurchaseStatus.Active || date < Today || date > contract.EndDate)
            return null;
        if (!RescheduleTimeEligible(contract, date, start, end, now))
            return null;
        var template = (await Templates(contract.PTUserId, ct)).Where(t => t.DayOfWeek == date.DayOfWeek && t.StartTime == start)
            .Select(t => (int?)t.Id).FirstOrDefault();
        if (template is null || await HasConflict(contract.PTUserId, contract.MemberUserId,
            [new(template.Value, date, start, end, "Available")], now, ct, ignoreSession: session.Id))
            return null;
        return template;
    }
}
