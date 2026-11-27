using System.Linq.Expressions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public static class PersonalTrainingRules
{
    public static List<(DateOnly Date, PTAvailability Slot)> Occurrences(
        DateOnly from, DateOnly through, IEnumerable<PTAvailability> slots, int count)
    {
        var lookup = slots.Where(slot => slot.DayOfWeek.HasValue)
            .OrderBy(slot => slot.StartTime).ToLookup(slot => slot.DayOfWeek!.Value);
        var result = new List<(DateOnly, PTAvailability)>();
        for (var day = from.DayNumber; day <= through.DayNumber && result.Count < count; day++)
        {
            var date = DateOnly.FromDayNumber(day);
            foreach (var slot in lookup[date.DayOfWeek].Take(count - result.Count))
                result.Add((date, slot));
        }
        return result;
    }

    public static bool Overlaps(TimeOnly start, TimeOnly end, TimeOnly otherStart, TimeOnly otherEnd) =>
        start < otherEnd && otherStart < end;

    public const int MakeUpGraceDays = 14;

    /// <summary>Any future start date is allowed; generated sessions also require 24 hours' notice.</summary>
    public static bool CanStartOn(DateOnly start, DateOnly today) => start > today;

    public static bool HasBookingNotice(DateOnly date, TimeOnly startTime, DateTime nowUtc) =>
        date.ToDateTime(startTime, DateTimeKind.Utc).AddHours(-7) >= nowUtc.AddHours(24);

    /// <summary>GBR-14: make-up units can be used until 14 days after the contract end date.</summary>
    public static DateOnly MakeUpDeadline(DateOnly endDate) => endDate.AddDays(MakeUpGraceDays);

    /// <summary>GBR-14 / UC 4.6: an open contract is still running until its end date, and for 14 more days only while
    /// a make-up unit is unused or its make-up session has not been held yet.</summary>
    public static Expression<Func<PTPurchase, bool>> StillRunning(DateOnly today)
    {
        var graceFrom = today.AddDays(-MakeUpGraceDays);
        return p => (p.Status == PTPurchaseStatus.Active || p.Status == PTPurchaseStatus.Frozen) &&
            (p.EndDate >= today || p.EndDate >= graceFrom && p.Sessions.Any(s =>
            s.SessionStatus == PTSessionStatus.Cancelled && s.CancelledAt != null && !s.IsCreditForfeited &&
            !p.Sessions.Any(r => r.ReplacementForSessionId == s.Id && r.SessionStatus != PTSessionStatus.Cancelled &&
                r.SessionStatus != PTSessionStatus.MakeUpPending && r.SessionStatus != PTSessionStatus.MakeUpRequested) ||
            s.ReplacementForSessionId != null && (s.SessionStatus == PTSessionStatus.Scheduled ||
                s.SessionStatus == PTSessionStatus.ReschedulePending || s.SessionStatus == PTSessionStatus.RescheduleRequested ||
                s.SessionStatus == PTSessionStatus.CancellationPending || s.SessionStatus == PTSessionStatus.CancellationRequested ||
                s.SessionStatus == PTSessionStatus.PendingFreeze || s.SessionStatus == PTSessionStatus.Disputed)));
    }

    private sealed record RunningCheck(DateOnly Day, Func<PTPurchase, bool> Check);
    private static RunningCheck? running;

    /// <summary>In-memory <see cref="StillRunning"/>, compiled once per day.</summary>
    public static bool IsStillRunning(PTPurchase p, DateOnly today)
    {
        var cached = running;
        if (cached is null || cached.Day != today)
            running = cached = new(today, StillRunning(today).Compile());
        return cached.Check(p);
    }

    public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    public static bool Reserved(PTSessionStatus status) =>
        status is PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested
            or PTSessionStatus.CancellationPending or PTSessionStatus.CancellationRequested
            or PTSessionStatus.PendingFreeze or PTSessionStatus.Disputed;

    public static PTSessionStatus PublicSessionStatus(PTSessionStatus status) => status switch
    {
        PTSessionStatus.CancellationPending => PTSessionStatus.ReschedulePending,
        PTSessionStatus.CancellationRequested => PTSessionStatus.RescheduleRequested,
        _ => status
    };

    public static bool Used(PTSessionStatus status) =>
        status is PTSessionStatus.Completed or PTSessionStatus.AutoCompleted or PTSessionStatus.NoShow;

    public static Expression<Func<PTSession, bool>> OccupiesTime(DateTime now) => session =>
        session.SessionStatus == PTSessionStatus.Scheduled ||
        session.SessionStatus == PTSessionStatus.ReschedulePending ||
        session.SessionStatus == PTSessionStatus.RescheduleRequested ||
        session.SessionStatus == PTSessionStatus.CancellationPending ||
        session.SessionStatus == PTSessionStatus.CancellationRequested ||
        session.SessionStatus == PTSessionStatus.PendingFreeze ||
        session.SessionStatus == PTSessionStatus.FreezeHold ||
        (session.SessionStatus == PTSessionStatus.CheckoutHold &&
            session.PTPurchase.Status == PTPurchaseStatus.PendingPayment &&
            session.PTPurchase.HoldExpiresAt > now);

    /// <summary>Completes the contract once nothing is left. Requires the sessions and purchased order item.</summary>
    public static void Settle(PTPurchase p)
    {
        var (used, reserved) = Credits(p.Sessions);
        if (used >= p.TotalSessions && reserved == 0)
            p.Status = PTPurchaseStatus.Completed;
    }

    /// <summary>GBR-103: after a 'PT Absent' report the PT can still record attendance until the end of the next day;
    /// the make-up unit becomes usable after that day.</summary>
    public static DateOnly TrainerResponseLastDay(DateOnly sessionDate) => sessionDate.AddDays(1);

    public static (int Used, int Reserved) Credits(IEnumerable<PTSession> sessions)
    {
        var used = 0;
        var reserved = 0;
        foreach (var session in sessions)
        {
            if (session.IsCreditForfeited || Used(session.SessionStatus))
                used += session.CreditUnits;
            else if (Reserved(session.SessionStatus))
                reserved += session.CreditUnits;
        }
        return (used, reserved);
    }
}
