namespace VFMS.Application.Services;

public static class TrainerReadinessRules
{
    public const int NotAcceptingLimitDays = 60;

    /// <summary>GBR-98: a trainer 'Not Accepting' for more than 60 consecutive days loses the PT role.</summary>
    public static bool HasExpired(DateOnly? notAcceptingSince, DateOnly today) =>
        notAcceptingSince is { } since && today.DayNumber - since.DayNumber > NotAcceptingLimitDays;
}
