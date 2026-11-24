using VFMS.Client.Components.Memberships;
using VFMS.Contracts.Yoga;
using Xunit;

namespace VFMS.ClientTests;

public sealed class YogaPlanAccessTests
{
    [Theory]
    [InlineData(false, false, null, "Beginner", YogaCardAction.BuyAndBook)]          // not signed in / no plan
    [InlineData(true, false, null, "Beginner", YogaCardAction.BuyAndBook)]           // member with a gym plan only
    [InlineData(true, true, "Intermediate", "Beginner", YogaCardAction.Book)]
    [InlineData(true, true, "Intermediate", "Intermediate", YogaCardAction.Book)]
    [InlineData(true, true, "Intermediate", "Advanced", YogaCardAction.Upgrade)]
    [InlineData(true, true, null, "Advanced", YogaCardAction.Book)]                  // old plan without a level books all
    [InlineData(false, true, "Advanced", "Beginner", YogaCardAction.BuyAndBook)]     // plan but no Member role: booking API refuses
    public void CardOffersBookingOnlyWhenThePlanCoversTheLevel(bool isMember, bool hasPlan, string? maxLevel, string classLevel, YogaCardAction expected)
    {
        var plan = hasPlan || maxLevel is not null ? new YogaMyPlanResponse(hasPlan, maxLevel) : null;

        Assert.Equal(expected, YogaPlanAccess.For(isMember, plan, classLevel));
    }

    [Fact]
    public void BuyLinkFiltersThePlansPageForThatClass()
    {
        var pending = new PendingYogaClass(7, "Hatha sáng", "Intermediate", new DateOnly(2026, 11, 30));

        Assert.Equal("/membership-plans?type=yoga&level=Intermediate&classId=7&date=2026-11-30&className=Hatha%20s%C3%A1ng", pending.PlansHref);
        Assert.Equal("/yoga/schedule?date=2026-11-30&classId=7", pending.ScheduleHref);
    }
}
