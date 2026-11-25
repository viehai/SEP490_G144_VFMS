using VFMS.Client.Components.Memberships;
using VFMS.Contracts.Yoga;
using Xunit;

namespace VFMS.ClientTests;

public sealed class YogaClassCardTests
{
    private static readonly DateTime Deadline = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private static YogaSessionResponse Session(int classId, DateOnly date, int hour, int free = 10, string status = "Scheduled",
        string classStatus = "Open", string name = "Hatha") =>
        new(date.DayNumber, classId, name, "Hatha", "Beginner", 1, "Studio 1", 3, "i1", "Lan",
            date, new TimeOnly(hour, 0), new TimeOnly(hour, 45), status, classStatus, 10, 10 - free, free,
            RegistrationDeadline: Deadline);

    [Fact]
    public void SessionsAreGroupedByClassWithOneSlotPerTimeAndWeekdaysInOrder()
    {
        var monday = new DateOnly(2026, 11, 16);
        var cards = YogaClassCard.FromSessions([
            Session(2, monday.AddDays(1), 18, name: "Vinyasa"),
            Session(1, monday.AddDays(6), 6),          // Sunday
            Session(1, monday, 6),                     // Monday
            Session(1, monday.AddDays(2), 6),          // Wednesday
            Session(1, monday.AddDays(13), 6),         // next Sunday: same weekday, listed once
            Session(1, monday.AddDays(5), 9),          // Saturday at another time
            Session(1, monday.AddDays(3), 6, status: "Cancelled"),
        ]);

        Assert.Equal([1, 2], cards.Select(c => c.ClassId));
        var hatha = cards[0];
        Assert.Equal("Studio 1 (Tầng 3)", hatha.Studio);
        Assert.Equal("/yoga/schedule?date=2026-11-16&classId=1", hatha.ScheduleHref);   // opens the week of the first session, not today
        Assert.Equal("Cơ bản", hatha.DifficultyName);
        Assert.Collection(hatha.Slots,
            slot => { Assert.Equal(new TimeOnly(6, 0), slot.Start); Assert.Equal(["T2", "T4", "CN"], slot.Days); },
            slot => { Assert.Equal(new TimeOnly(9, 0), slot.Start); Assert.Equal(["T7"], slot.Days); });
    }

    [Theory]
    [InlineData(10, "Open", false, YogaClassBadge.Open)]
    [InlineData(3, "Open", false, YogaClassBadge.Open)]
    [InlineData(2, "Open", false, YogaClassBadge.AlmostFull)]   // 20% of 10 mats
    [InlineData(0, "Open", false, YogaClassBadge.Full)]
    [InlineData(5, "Full", false, YogaClassBadge.Full)]
    [InlineData(10, "Open", true, YogaClassBadge.Closed)]
    public void BadgeFollowsSeatsAndTheRegistrationDeadline(int free, string classStatus, bool afterDeadline, YogaClassBadge expected)
    {
        var card = YogaClassCard.FromSessions([Session(1, new DateOnly(2026, 11, 16), 6, free, classStatus: classStatus)])[0];

        Assert.Equal(expected, card.Badge(afterDeadline ? Deadline : Deadline.AddMinutes(-1)));
    }
}
