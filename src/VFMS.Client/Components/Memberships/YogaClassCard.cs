using VFMS.Contracts.Yoga;

namespace VFMS.Client.Components.Memberships;

/// <summary>One time slot of a class and the weekdays it runs on, e.g. T2 T4 T6 06:30–07:45.</summary>
public sealed record YogaClassSlot(TimeOnly Start, TimeOnly End, IReadOnlyList<string> Days);

/// <summary>A yoga class as the home page card shows it, built from its published sessions.</summary>
public sealed record YogaClassCard(
    int ClassId, string Name, string TypeName, string Difficulty, string Studio, string Instructor,
    IReadOnlyList<YogaClassSlot> Slots, int MaxParticipants, int FreeSeats, string ClassStatus, DateTime? RegistrationDeadline)
{
    /// <summary>Groups sessions by class, earliest class first. Seats are counted per class, so they come from one session.</summary>
    public static IReadOnlyList<YogaClassCard> FromSessions(IEnumerable<YogaSessionResponse> sessions) =>
        sessions.Where(s => s.Status != "Cancelled")
            .GroupBy(s => s.YogaClassId)
            .Select(g =>
            {
                var ordered = g.OrderBy(s => s.ScheduledDate).ThenBy(s => s.StartTime).ToArray();
                var first = ordered[0];
                var slots = ordered.GroupBy(s => (s.StartTime, s.EndTime))
                    .OrderBy(t => t.Key.StartTime)
                    .Select(t => new YogaClassSlot(t.Key.StartTime, t.Key.EndTime,
                        t.Select(s => s.ScheduledDate.DayOfWeek).Distinct().OrderBy(d => ((int)d + 6) % 7).Select(Day).ToArray()))
                    .ToArray();
                return (Start: (first.ScheduledDate, first.StartTime), Card: new YogaClassCard(
                    first.YogaClassId, first.ClassName, first.TypeName, first.DifficultyLevel,
                    first.FloorNumber > 0 ? $"{first.StudioName} (Tầng {first.FloorNumber})" : first.StudioName,
                    first.InstructorName, slots, first.MaxParticipants, first.FreeSeats, first.ClassStatus, first.RegistrationDeadline));
            })
            .OrderBy(x => x.Start).Select(x => x.Card).ToArray();

    /// <summary>
    /// The status pill: registration closed; full (new bookings go on the waitlist);
    /// almost full (at most 20% of the mats free); otherwise seats left.
    /// </summary>
    public YogaClassBadge Badge(DateTime nowUtc) =>
        RegistrationDeadline is { } deadline && nowUtc >= deadline ? YogaClassBadge.Closed
        : ClassStatus == "Full" || FreeSeats <= 0 ? YogaClassBadge.Full
        : FreeSeats * 5 <= MaxParticipants ? YogaClassBadge.AlmostFull
        : YogaClassBadge.Open;

    public string DifficultyName => Difficulty switch
    {
        "Beginner" => "Cơ bản",
        "Intermediate" => "Trung bình",
        "Advanced" => "Nâng cao",
        _ => Difficulty,
    };

    private static string Day(DayOfWeek day) => day == DayOfWeek.Sunday ? "CN" : $"T{(int)day + 1}";
}

public enum YogaClassBadge { Open, AlmostFull, Full, Closed }
