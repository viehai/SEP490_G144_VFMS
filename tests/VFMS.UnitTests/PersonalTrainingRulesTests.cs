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
    public void Back_to_back_sessions_require_exactly_fifteen_minutes_of_buffer()
    {
        Assert.True(PersonalTrainingRules.Overlaps(new(18, 0), new(19, 0), new(19, 14), new(20, 14)));
        Assert.False(PersonalTrainingRules.Overlaps(new(18, 0), new(19, 0), new(19, 15), new(20, 15)));
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
}
