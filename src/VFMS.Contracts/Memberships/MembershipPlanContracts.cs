namespace VFMS.Contracts.Memberships;

public static class MembershipPlanStatuses
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
}

/// <summary>The service a plan sells. Fixed when the plan is created.</summary>
public static class MembershipPlanServiceTypes
{
    public const string Gym = "GYM";
    public const string Yoga = "YOGA";
}

public enum MembershipFreezeAllowanceFilter
{
    Default,
    Disabled,
    Custom
}

public sealed class MembershipPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? TargetAudience { get; set; }
    public int? DurationValue { get; set; }
    public MembershipDurationUnit DurationUnit { get; set; }
    /// <summary>Compatibility with day-only plan clients.</summary>
    public int? DurationInDays { get; set; }
    public int? MaxFreezeCount { get; set; }
    public int? MinFreezeDaysPerRequest { get; set; }
    public int? MaxFreezeDaysPerRequest { get; set; }
    public int? FreezeAllowanceOverrideValue { get; set; }
    public MembershipDurationUnit FreezeAllowanceOverrideUnit { get; set; }
    public decimal? Price { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>GYM or YOGA. Used on create only: an update keeps the plan's type.</summary>
    public string ServiceType { get; set; } = MembershipPlanServiceTypes.Gym;

    /// <summary>YOGA plans only, required: Beginner / Intermediate / Advanced, the highest class level it books.</summary>
    public string? YogaDifficultyLevel { get; set; }
}

public sealed class MembershipPlanStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public sealed class MembershipPlanListRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }

    /// <summary>GYM or YOGA; empty lists both.</summary>
    public string? ServiceType { get; set; }
    public MembershipDurationUnit? DurationUnit { get; set; }
    public MembershipFreezeAllowanceFilter? FreezeAllowanceFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record MembershipPlanResponse(
    int Id,
    string Name,
    string? Description,
    int? DurationInDays,
    decimal Price,
    string Status,
    int SubscriberCount,
    int? DurationInMonths,
    string ServiceType = MembershipPlanServiceTypes.Gym,
    string? YogaDifficultyLevel = null)
{
    public int? DurationValue => DurationInMonths ?? DurationInDays;
    public MembershipDurationUnit DurationUnit => DurationInMonths.HasValue ? MembershipDurationUnit.Month : MembershipDurationUnit.Day;
    public string? TargetAudience { get; init; }
    public int? MaxFreezeCount { get; init; }
    public int? MinFreezeDaysPerRequest { get; init; }
    public int? MaxFreezeDaysPerRequest { get; init; }
    public int? FreezeAllowanceOverrideDays { get; init; }
    public MembershipDurationUnit FreezeAllowanceOverrideUnit { get; init; }
    public int? FreezeAllowanceOverrideValue => FreezeAllowanceOverrideUnit == MembershipDurationUnit.Month
        ? FreezeAllowanceOverrideDays / MembershipFreezeDuration.DaysPerMonth : FreezeAllowanceOverrideDays;
}

public sealed record MembershipPlanCatalogItemResponse(
    int Id,
    string Name,
    string? Description,
    int? DurationInDays,
    decimal Price,
    string Status,
    int? DurationInMonths,
    string ServiceType = MembershipPlanServiceTypes.Gym,
    string? YogaDifficultyLevel = null)
{
    public bool IsSingleService { get; init; } = true;
    public string? TargetAudience { get; init; }
    public int? DurationValue => DurationInMonths ?? DurationInDays;
    public MembershipDurationUnit DurationUnit => DurationInMonths.HasValue ? MembershipDurationUnit.Month : MembershipDurationUnit.Day;
}

public sealed record MembershipPlanDeleteResponse(bool Deactivated, string Message);
