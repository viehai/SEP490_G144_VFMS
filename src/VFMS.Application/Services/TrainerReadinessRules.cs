namespace VFMS.Application.Services;

public static class TrainerReadinessRules
{
    public static bool IsAccepting(bool current, bool? pending, DateOnly? effectiveDate, DateOnly today) =>
        pending is not null && effectiveDate <= today ? pending.Value : current;

    public static bool HasExpired(DateOnly? firstReadyDate, DateOnly? lastContractEndDate, DateOnly today)
    {
        var start = lastContractEndDate ?? firstReadyDate;
        return start is not null && start.Value.AddMonths(2) <= today;
    }
}
