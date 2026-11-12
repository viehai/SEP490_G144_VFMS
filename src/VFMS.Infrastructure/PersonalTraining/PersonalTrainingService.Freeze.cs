using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.PersonalTraining;

public sealed partial class PersonalTrainingService
{
    private static bool Approved(PTFreeze f) => f.RequestStatus is PTFreezeRequestStatus.Approved or
        PTFreezeRequestStatus.Active or PTFreezeRequestStatus.Completed;

    private static int FrozenDays(PTPurchase p) => p.PTFreezes.Where(Approved)
        .Sum(f => (f.ActualEndDate ?? f.RequestedEndDate).DayNumber -
            (f.ActualStartDate ?? f.RequestedStartDate).DayNumber);

    private static int RemainingFreezeDays(PTPurchase p)
    {
        var pendingDays = p.PTFreezes.Where(f => f.RequestStatus == PTFreezeRequestStatus.Pending)
            .Sum(f => f.RequestedEndDate.DayNumber - f.RequestedStartDate.DayNumber);
        return Math.Max(0, p.FreezeMaxTotalDaysSnapshot - FrozenDays(p) - pendingDays);
    }

    private PTFreezeResponse FreezeResponse(PTFreeze f, PTPurchase p)
    {
        var state = f.RequestStatus;
        if (Approved(f))
            state = Today >= f.ActualEndDate ? PTFreezeRequestStatus.Completed :
                Today >= f.ActualStartDate ? PTFreezeRequestStatus.Active : PTFreezeRequestStatus.Approved;
        return new(f.Id, p.Id, p.MemberUserId, p.PTUserId, f.RequestedStartDate,
            f.ActualEndDate ?? f.RequestedEndDate, state.ToString(), f.Reason,
            f.ReviewNote, f.ReviewedAt);
    }

    public async Task<Result<IReadOnlyList<PTFreezeResponse>>> Freezes(string actor, bool managerView, CancellationToken ct)
    {
        var user = await User(actor, managerView ? UserRole.Manager : UserRole.Member, ct);
        if (user is null)
            return Forbidden<IReadOnlyList<PTFreezeResponse>>();
        var requests = await db.PTFreezes.AsNoTracking().Include(f => f.PTPurchase)
            .Where(f => managerView
                ? f.PTPurchase.PTUser.GymId == user.GymId && f.RequestStatus == PTFreezeRequestStatus.Pending
                : f.PTPurchase.MemberUserId == actor)
            .OrderByDescending(f => f.Id).Take(100).ToListAsync(ct);
        return Result<IReadOnlyList<PTFreezeResponse>>.Success(requests.Select(f => FreezeResponse(f, f.PTPurchase)).ToArray());
    }

    public Task<Result<PTFreezeResponse>> RequestFreeze(string actor, int id, PTFreezeRequest input, CancellationToken ct) =>
        Write(async () =>
        {
            if (await User(actor, UserRole.Member, ct) is null)
                return Forbidden<PTFreezeResponse>();
            if (string.IsNullOrWhiteSpace(input.Reason))
                return Invalid<PTFreezeResponse>("Cần nhập lý do bảo lưu.");
            var p = await ContractsWithSessions.SingleOrDefaultAsync(p => p.Id == id && p.MemberUserId == actor, ct);
            if (p is null)
                return Missing<PTFreezeResponse>();
            if (p.PTFreezes.Any(f => f.RequestStatus == PTFreezeRequestStatus.Pending || Approved(f)))
                return Conflict<PTFreezeResponse>("Hợp đồng đã có yêu cầu đang chờ hoặc đã được duyệt.");
            if (EffectiveStatus(p) != PTPurchaseStatus.Active || input.StartDate < Today ||
                input.StartDate > p.EndDate || input.StartDate < p.StartDate)
                return Conflict<PTFreezeResponse>("Ngày bảo lưu hoặc trạng thái hợp đồng không hợp lệ.");
            var minimum = p.FreezeMinimumDaysSnapshot;
            var cap = p.FreezeMaxTotalDaysSnapshot;
            var days = input.ResumeDate.DayNumber - input.StartDate.DayNumber;
            if (days <= 0 || days < minimum || days > RemainingFreezeDays(p) ||
                p.PTFreezes.Any(f => (Approved(f) || f.RequestStatus == PTFreezeRequestStatus.Pending) &&
                    f.RequestedStartDate < input.ResumeDate && input.StartDate < (f.ActualEndDate ?? f.RequestedEndDate)))
                return Conflict<PTFreezeResponse>("Khoảng bảo lưu bị trùng hoặc không đáp ứng hạn mức.");
            var freeze = new PTFreeze
            {
                PTPurchaseId = id, RequestedByUserId = actor, RequestedStartDate = input.StartDate,
                RequestedEndDate = input.ResumeDate, Reason = input.Reason.Trim(),
                MinimumDaysSnapshot = minimum, MaxDaysSnapshot = cap
            };
            db.PTFreezes.Add(freeze);
            foreach (var session in p.Sessions.Where(s => s.SessionDate >= input.StartDate && s.SessionDate < input.ResumeDate &&
                s.SessionStatus is PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending))
            {
                ClearProposal(session);
                session.SessionStatus = PTSessionStatus.PendingFreeze;
            }
            await db.SaveChangesAsync(ct);
            Notify(actor, "Đã gửi yêu cầu bảo lưu PT", "Các buổi trong khoảng bảo lưu được bảo vệ trong lúc chờ duyệt.",
                nameof(PTFreeze), freeze.Id);
            await db.SaveChangesAsync(ct);
            return Result<PTFreezeResponse>.Success(FreezeResponse(freeze, p), 201);
        }, ct);

    private Task<PTPurchase?> ManagerFreeze(string actor, int freezeId, CancellationToken ct) =>
        ContractsWithSessions.SingleOrDefaultAsync(p => p.PTFreezes.Any(f => f.Id == freezeId) &&
            ActiveUsers.Any(u => u.UserId == actor && u.GymId == p.PTUser.GymId &&
                u.UserRoles.Any(r => r.Role.RoleName == nameof(UserRole.Manager))), ct);

    private sealed record Replacement(PTSession Original, PTCalendarSlot Slot);
    private sealed record FreezeProjection(DateOnly End, IReadOnlyList<Replacement> Replacements);

    private async Task<FreezeProjection?> ProjectFreeze(PTPurchase p, PTFreeze f, CancellationToken ct)
    {
        var days = f.RequestedEndDate.DayNumber - f.RequestedStartDate.DayNumber;
        if (p.EndDate.DayNumber > DateOnly.MaxValue.DayNumber - days)
            return null;
        var end = p.EndDate.AddDays(days);
        var start = f.RequestedEndDate > Today ? f.RequestedEndDate : Today;
        var affected = p.Sessions.Where(s => s.SessionStatus == PTSessionStatus.PendingFreeze &&
                s.SessionDate >= f.RequestedStartDate && s.SessionDate < f.RequestedEndDate)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).ToArray();
        var result = new List<Replacement>();
        var pattern = JsonSerializer.Deserialize<PTSlotInput[]>(p.WeeklyPatternJson ?? "[]")!;
        var recurring = pattern.Select(x => new PTAvailability { DayOfWeek = x.DayOfWeek, StartTime = x.StartTime });
        var otherWindows = p.PTFreezes.Where(x => x.Id != f.Id &&
            (Approved(x) || x.RequestStatus == PTFreezeRequestStatus.Pending)).ToArray();
        var now = Now;
        foreach (var (date, slot) in PersonalTrainingRules.Occurrences(start, end, recurring, int.MaxValue))
        {
            if (result.Count == affected.Length)
                break;
            var original = affected[result.Count];
            var finish = slot.StartTime.AddHours(original.CreditUnits);
            var candidate = new PTCalendarSlot(0, date, slot.StartTime, finish, "Available");
            if (Utc(date, slot.StartTime) <= now || !InHours(p.PTUser.Gym, slot.StartTime, finish) ||
                result.Any(x => x.Slot.Date == date) ||
                otherWindows.Any(x => x.RequestedStartDate <= date && date < (x.ActualEndDate ?? x.RequestedEndDate)) ||
                await HasConflict(p.PTUserId, p.MemberUserId, [candidate], now, ct))
                continue;
            result.Add(new(original, candidate));
        }
        var lastDate = result.Select(x => x.Slot.Date).Concat(p.Sessions.Where(s => PersonalTrainingRules.Reserved(s.SessionStatus) &&
                !affected.Contains(s)).Select(s => s.SessionDate)).DefaultIfEmpty(Today).Max();
        if (result.Count != affected.Length ||
            !await Coverage(p.MemberUserId).AnyAsync(m => m.StartDate <= Today && m.EndDate >= lastDate, ct))
            return null;
        return new(end, result);
    }

    public async Task<Result<PTFreezePreview>> PreviewFreeze(string actor, int id, CancellationToken ct)
    {
        var purchase = await ManagerFreeze(actor, id, ct);
        if (purchase is null)
            return Missing<PTFreezePreview>();
        var f = purchase.PTFreezes.Single(x => x.Id == id);
        if (f.RequestStatus != PTFreezeRequestStatus.Pending)
            return Conflict<PTFreezePreview>("Yêu cầu đã được xử lý.");
        var projection = await ProjectFreeze(purchase, f, ct);
        return projection is null ? Conflict<PTFreezePreview>("Không đủ lịch thay thế hoặc thời hạn gói Gym.") :
            Result<PTFreezePreview>.Success(new(projection.End, projection.Replacements.Select(r => r.Slot).ToArray()));
    }

    public Task<Result<PTFreezeResponse>> DecideFreeze(string actor, int id, PTFreezeDecision input, CancellationToken ct) =>
        Write(async () =>
        {
            var purchase = await ManagerFreeze(actor, id, ct);
            if (purchase is null)
                return Missing<PTFreezeResponse>();
            var f = purchase.PTFreezes.Single(x => x.Id == id);
            if (f.RequestStatus != PTFreezeRequestStatus.Pending)
                return Conflict<PTFreezeResponse>("Yêu cầu đã được xử lý.");
            if (input.Approve)
            {
                var projection = await ProjectFreeze(purchase, f, ct);
                if (projection is null)
                    return Conflict<PTFreezeResponse>("Không đủ lịch thay thế hoặc thời hạn gói Gym.");
                purchase.EndDate = projection.End;
                foreach (var (original, slot) in projection.Replacements)
                {
                    original.SessionStatus = PTSessionStatus.Cancelled;
                    original.CancelledAt = Now;
                    purchase.Sessions.Add(new PTSession
                    {
                        PTUserId = original.PTUserId, MemberUserId = original.MemberUserId, SessionDate = slot.Date,
                        StartTime = slot.StartTime, EndTime = slot.EndTime, CreditUnits = original.CreditUnits,
                        ReplacementForSessionId = original.Id
                    });
                }
                f.ActualStartDate = f.RequestedStartDate;
                f.ActualEndDate = f.RequestedEndDate;
                f.RequestStatus = PTFreezeRequestStatus.Approved;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.Reason))
                    return Invalid<PTFreezeResponse>("Cần nhập lý do từ chối.");
                f.RequestStatus = PTFreezeRequestStatus.Rejected;
                foreach (var session in purchase.Sessions.Where(s => s.SessionStatus == PTSessionStatus.PendingFreeze &&
                    s.SessionDate >= f.RequestedStartDate && s.SessionDate < f.RequestedEndDate))
                    session.SessionStatus = PTSessionStatus.Scheduled;
            }
            f.ReviewedByUserId = actor;
            f.ReviewedAt = Now;
            f.ReviewNote = input.Reason?.Trim();
            var message = input.Approve ? "Yêu cầu bảo lưu đã được duyệt. Hãy kiểm tra lịch cập nhật." :
                "Yêu cầu bảo lưu bị từ chối. Lịch cũ được giữ.";
            Notify(purchase.MemberUserId, "Kết quả bảo lưu PT", message, nameof(PTFreeze), id);
            Notify(purchase.PTUserId, "Kết quả bảo lưu PT", message, nameof(PTFreeze), id);
            Audit(actor, nameof(PTFreeze), id, f.RequestStatus.ToString());
            await db.SaveChangesAsync(ct);
            return Result<PTFreezeResponse>.Success(FreezeResponse(f, purchase));
        }, ct);

}
