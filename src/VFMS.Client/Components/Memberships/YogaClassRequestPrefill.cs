using VFMS.Contracts.Yoga;

namespace VFMS.Client.Components.Memberships;

/// <summary>
/// Class opening requests (GBR-128..GBR-130): labels for both pages, and how an approved request fills the
/// Manager's create-class form and the weekly-schedule form that follows it.
/// </summary>
public static class YogaClassRequestPrefill
{
    private static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    /// <summary>Monday first, as the weekday buttons show them.</summary>
    public static readonly (DayOfWeek Day, string Label)[] WeekDays =
    [
        (DayOfWeek.Monday, "T2"), (DayOfWeek.Tuesday, "T3"), (DayOfWeek.Wednesday, "T4"), (DayOfWeek.Thursday, "T5"),
        (DayOfWeek.Friday, "T6"), (DayOfWeek.Saturday, "T7"), (DayOfWeek.Sunday, "CN")
    ];

    public static string StatusLabel(string status) => status switch
    {
        "Pending" => "Chờ duyệt",
        "Approved" => "Đã duyệt",
        "Rejected" => "Bị từ chối",
        "Withdrawn" => "Đã rút",
        _ => status
    };

    /// <summary>"T2, T4, T6" in week order.</summary>
    public static string DaysLabel(IEnumerable<DayOfWeek> days) =>
        string.Join(", ", WeekDays.Where(w => days.Contains(w.Day)).Select(w => w.Label));

    /// <summary>The request doesn't carry a registration deadline: default to 23:59 (gym time) the day before the class starts.</summary>
    public static DateTime DefaultDeadlineLocal(DateOnly startDate) => startDate.AddDays(-1).ToDateTime(new TimeOnly(23, 59));

    /// <summary>The create-class body the Manager reviews: the request's fields, its instructor, and the default deadline (UTC).</summary>
    public static YogaClassRequest ToClassRequest(YogaClassOpeningRequestResponse r) => new(
        r.YogaClassTypeId, r.ClassName, r.InstructorUserId, r.DifficultyLevel, r.SessionsPerWeek,
        r.MinParticipants, r.MaxParticipants, r.DurationMinutes,
        DateTime.SpecifyKind(DefaultDeadlineLocal(r.StartDate) - GymOffset, DateTimeKind.Utc),
        r.StartDate, r.EndDate, r.Description);

    /// <summary>Client-side check before submit (the API checks it too): the weekday count must equal sessions per week.</summary>
    public static string? DaysProblem(IReadOnlyCollection<DayOfWeek> days, int sessionsPerWeek) =>
        days.Count == sessionsPerWeek ? null : $"Lớp {sessionsPerWeek} buổi/tuần: hãy chọn đúng {sessionsPerWeek} ngày (đang chọn {days.Count}).";

    /// <summary>Client-side check: the preferred studio must have a mat for every participant.</summary>
    public static string? StudioProblem(YogaStudioResponse? studio, int maxParticipants) =>
        studio is not null && studio.Capacity < maxParticipants
            ? $"Phòng {studio.StudioName} chỉ có {studio.Capacity} thảm, ít hơn số học viên tối đa ({maxParticipants})."
            : null;
}
