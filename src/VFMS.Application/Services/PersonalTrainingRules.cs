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

    /// <summary>GBR-12: the start week begins on a Monday and the hold must be made no later than the Friday before it.</summary>
    public static bool CanStartOn(DateOnly monday, DateOnly today) =>
        monday.DayOfWeek == DayOfWeek.Monday && today <= monday.AddDays(-3);

    /// <summary>GBR-14: make-up units can be used until 14 days after the contract end date.</summary>
    public static DateOnly MakeUpDeadline(DateOnly endDate) => endDate.AddDays(MakeUpGraceDays);

    public static bool Reserved(PTSessionStatus status) =>
        status is PTSessionStatus.Scheduled or PTSessionStatus.ReschedulePending or PTSessionStatus.RescheduleRequested
            or PTSessionStatus.PendingFreeze or PTSessionStatus.Disputed;

    public static bool Used(PTSessionStatus status) =>
        status is PTSessionStatus.Completed or PTSessionStatus.AutoCompleted or PTSessionStatus.NoShow;

    public static Expression<Func<PTSession, bool>> OccupiesTime(DateTime now) => session =>
        session.SessionStatus == PTSessionStatus.Scheduled ||
        session.SessionStatus == PTSessionStatus.ReschedulePending ||
        session.SessionStatus == PTSessionStatus.RescheduleRequested ||
        session.SessionStatus == PTSessionStatus.PendingFreeze ||
        session.SessionStatus == PTSessionStatus.FreezeHold ||
        (session.SessionStatus == PTSessionStatus.CheckoutHold &&
            session.PTPurchase.Status == PTPurchaseStatus.PendingPayment &&
            session.PTPurchase.HoldExpiresAt > now);

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
