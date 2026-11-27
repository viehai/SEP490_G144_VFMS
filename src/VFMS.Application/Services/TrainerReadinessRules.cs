using VFMS.Domain.Entities;

namespace VFMS.Application.Services;

public static class TrainerReadinessRules
{
    public const int NotAcceptingLimitDays = 60;
    public const int AcceptingResetDays = 10;

    public static void AccumulateInactiveDays(TrainerProfile profile, DateOnly today, bool hasRunningContract)
    {
        if (profile.AcceptingNewMembers)
            return;
        if (!hasRunningContract && (profile.NotAcceptingCountFrom ?? profile.NotAcceptingSince) is { } from)
            profile.NotAcceptingAccumulatedDays += Math.Max(0, today.DayNumber - from.DayNumber);
        profile.NotAcceptingCountFrom = today;
    }

    public static void SetAccepting(TrainerProfile profile, bool accepting, DateOnly today, bool hasRunningContract)
    {
        if (profile.AcceptingNewMembers == accepting)
            return;
        if (accepting)
        {
            AccumulateInactiveDays(profile, today, hasRunningContract);
            profile.NotAcceptingCountFrom = null;
        }
        else
        {
            if (profile.AcceptingSince is { } since && today.DayNumber - since.DayNumber >= AcceptingResetDays)
                profile.NotAcceptingAccumulatedDays = 0;
            profile.NotAcceptingCountFrom = today;
        }
        profile.AcceptingNewMembers = accepting;
        profile.ReadinessSince = today;
    }

    public static bool HasExpired(TrainerProfile profile) =>
        !profile.AcceptingNewMembers && profile.NotAcceptingAccumulatedDays > NotAcceptingLimitDays;
}
