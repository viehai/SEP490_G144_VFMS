using VFMS.Client.Pages.Member;
using VFMS.Contracts.Yoga;
using Xunit;

namespace VFMS.ClientTests;

public sealed class MyYogaBookingsCountdownTests
{
    // 25/11/2026 14:00 gym time (UTC+7).
    private static readonly DateTime Now = new(2026, 11, 25, 7, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(25, 17, 30, "Hôm nay · còn 3 giờ", true)]
    [InlineData(25, 14, 20, "Hôm nay · còn 20 phút", true)]
    [InlineData(25, 13, 0, "Đang diễn ra", true)]
    [InlineData(26, 7, 0, "Ngày mai", true)]
    [InlineData(30, 7, 0, "Còn 5 ngày", false)]
    public void NextSessionCountdownUsesGymTime(int day, int hour, int minute, string text, bool soon)
    {
        var booking = new MyYogaBookingResponse(1, 1, "Vinyasa", new DateOnly(2026, 11, day), new TimeOnly(hour, minute),
            new TimeOnly(hour, minute).AddHours(1), "Lotus", 3, "Confirmed");
        Assert.Equal((text, soon), MyYogaBookings.Countdown(booking, Now));
    }

    // "Mua gói mới" on a held booking or an expired yoga plan: plans of that level, never a class (it is already booked).
    [Theory]
    [InlineData("Intermediate", "/membership-plans?type=yoga&level=Intermediate")]
    [InlineData(null, "/membership-plans?type=yoga")]
    [InlineData("Unknown", "/membership-plans?type=yoga")]
    public void BuyNewLinksToPlansOfTheLevel(string? level, string href) =>
        Assert.Equal(href, MyYogaBookings.BuyNewHref(level));
}
