using VFMS.Contracts.Yoga;

namespace VFMS.Client.Components.Memberships;

/// <summary>One time slot of a class and the weekdays it runs on, e.g. T2 T4 T6 06:30–07:45.</summary>
public sealed record YogaClassSlot(TimeOnly Start, TimeOnly End, IReadOnlyList<string> Days);

/// <summary>A yoga class as the home page card shows it, built from its published sessions.</summary>
public sealed record YogaClassCard(
    int ClassId, string Name, string TypeName, string Difficulty, string Studio, string Instructor,
    IReadOnlyList<YogaClassSlot> Slots, int MaxParticipants, int FreeSeats, string ClassStatus, DateTime? RegistrationDeadline, DateOnly FirstDate)
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
                    first.InstructorName, slots, first.MaxParticipants, first.FreeSeats, first.ClassStatus, first.RegistrationDeadline, first.ScheduledDate));
            })
            .OrderBy(x => x.Start).Select(x => x.Card).ToArray();

    /// <summary>The timetable opened on the week of this class's first session, with the class highlighted.</summary>
    public string ScheduleHref => ScheduleLink(ClassId, FirstDate);

    public static string ScheduleLink(int classId, DateOnly date) => $"/yoga/schedule?date={date:yyyy-MM-dd}&classId={classId}";

    /// <summary>
    /// The status pill: registration closed; full (new bookings go on the waitlist);
    /// almost full (at most 20% of the mats free); otherwise seats left.
    /// </summary>
    public YogaClassBadge Badge(DateTime nowUtc) =>
        RegistrationDeadline is { } deadline && nowUtc >= deadline ? YogaClassBadge.Closed
        : ClassStatus == "Full" || FreeSeats <= 0 ? YogaClassBadge.Full
        : FreeSeats * 5 <= MaxParticipants ? YogaClassBadge.AlmostFull
        : YogaClassBadge.Open;

    public static string BadgeText(YogaClassBadge badge) => badge switch
    {
        YogaClassBadge.Closed => "HẾT HẠN",
        YogaClassBadge.Full => "HẾT CHỖ",
        YogaClassBadge.AlmostFull => "SẮP ĐẦY",
        _ => "CÒN CHỖ",
    };

    public static string BadgeColor(YogaClassBadge badge) => badge switch
    {
        YogaClassBadge.Closed => "bg-[#6F6966]",
        YogaClassBadge.Full => "bg-[#B83A3A]",
        YogaClassBadge.AlmostFull => "bg-[#B45309]",
        _ => "bg-[#15803D]",
    };

    /// <summary>Weekdays the class runs on, across all its time slots.</summary>
    public int SessionsPerWeek => Slots.SelectMany(slot => slot.Days).Distinct().Count();

    public int Minutes => Slots.Count == 0 ? 0 : (int)(Slots[0].End - Slots[0].Start).TotalMinutes;

    public string DifficultyName => LevelName(Difficulty);

    public static string LevelName(string level) => level switch
    {
        "Beginner" => "Cơ bản",
        "Intermediate" => "Trung bình",
        "Advanced" => "Nâng cao",
        _ => level,
    };

    /// <summary>Vietnamese label for a yoga class, session, booking or studio status code.</summary>
    public static string StatusName(string status) => status switch
    {
        "Draft" => "Nháp", "Open" => "Đang mở", "Full" => "Hết chỗ", "Cancelled" => "Đã hủy", "Completed" => "Đã kết thúc",
        "Scheduled" => "Theo lịch", "Confirmed" => "Đã đặt", "Waitlisted" => "Đang chờ", "Active" => "Hoạt động", "Inactive" => "Ngừng",
        _ => status,
    };

    public static string DayName(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ 2", DayOfWeek.Tuesday => "Thứ 3", DayOfWeek.Wednesday => "Thứ 4", DayOfWeek.Thursday => "Thứ 5",
        DayOfWeek.Friday => "Thứ 6", DayOfWeek.Saturday => "Thứ 7", _ => "Chủ nhật"
    };

    private static string Day(DayOfWeek day) => day == DayOfWeek.Sunday ? "CN" : $"T{(int)day + 1}";
}

public enum YogaClassBadge { Open, AlmostFull, Full, Closed }
