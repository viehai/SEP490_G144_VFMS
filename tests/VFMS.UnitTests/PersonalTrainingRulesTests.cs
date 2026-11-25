using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class PersonalTrainingRulesTests
{
    [Fact]
    public void Preview_generates_only_the_requested_credits_on_recurring_days()
    {
        var start = new DateOnly(2026, 11, 9);
        var slots = new[]
        {
            new PTAvailability { Id = 1, DayOfWeek = DayOfWeek.Monday },
            new PTAvailability { Id = 2, DayOfWeek = DayOfWeek.Wednesday },
            new PTAvailability { Id = 3, DayOfWeek = DayOfWeek.Friday }
        };

        var dates = PersonalTrainingRules.Occurrences(start, start.AddDays(60), slots, 4);

        Assert.Equal(new[] { 9, 11, 13, 16 }, dates.Select(item => item.Date.Day));
    }

    [Fact]
    public void Back_to_back_sessions_can_share_an_endpoint()
    {
        Assert.True(PersonalTrainingRules.Overlaps(new(18, 0), new(19, 0), new(18, 59), new(19, 59)));
        Assert.False(PersonalTrainingRules.Overlaps(new(18, 0), new(19, 0), new(19, 0), new(20, 0)));
    }

    [Fact]
    public void Pending_changes_keep_the_original_credits_reserved()
    {
        var sessions = new[]
        {
            new PTSession { SessionStatus = PTSessionStatus.ReschedulePending, CreditUnits = 1 },
            new PTSession { SessionStatus = PTSessionStatus.PendingFreeze, CreditUnits = 2 },
            new PTSession { SessionStatus = PTSessionStatus.Cancelled, CreditUnits = 2, IsCreditForfeited = true },
            new PTSession { SessionStatus = PTSessionStatus.Cancelled, CreditUnits = 1 }
        };

        Assert.Equal((2, 3), PersonalTrainingRules.Credits(sessions));
    }

    [Theory]
    [InlineData("2026-11-17", "2026-11-23", true)]
    [InlineData("2026-11-20", "2026-11-23", true)]
    [InlineData("2026-11-21", "2026-11-23", true)]
    [InlineData("2026-11-21", "2026-11-30", true)]
    [InlineData("2026-11-16", "2026-11-16", false)]  // Same-day starts cannot provide 24 hours' notice.
    [InlineData("2026-11-17", "2026-11-24", true)]
    [InlineData("2026-11-17", "2026-11-18", true)]
    [InlineData("2026-11-17", "2026-11-16", false)]
    public void Start_date_can_be_any_future_weekday(string today, string start, bool allowed) =>
        Assert.Equal(allowed, PersonalTrainingRules.CanStartOn(DateOnly.Parse(start), DateOnly.Parse(today)));

    [Fact]
    public void Auto_completed_sessions_count_as_used_and_disputes_stay_reserved()
    {
        var sessions = new[]
        {
            new PTSession { SessionStatus = PTSessionStatus.AutoCompleted, CreditUnits = 1 },
            new PTSession { SessionStatus = PTSessionStatus.Disputed, CreditUnits = 1 },
            new PTSession { SessionStatus = PTSessionStatus.FreezeHold, CreditUnits = 1 }
        };
        Assert.Equal((1, 1), PersonalTrainingRules.Credits(sessions));
        Assert.Equal(new DateOnly(2026, 12, 15), PersonalTrainingRules.MakeUpDeadline(new DateOnly(2026, 12, 1)));
    }
}
