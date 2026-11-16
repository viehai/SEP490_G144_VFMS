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

public sealed class MembershipPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DurationInDays { get; set; }
    public decimal? Price { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>GYM or YOGA. Used on create only: an update keeps the plan's type.</summary>
    public string ServiceType { get; set; } = MembershipPlanServiceTypes.Gym;
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
    string ServiceType = MembershipPlanServiceTypes.Gym);

public sealed record MembershipPlanCatalogItemResponse(
    int Id,
    string Name,
    string? Description,
    int? DurationInDays,
    decimal Price,
    string Status,
    int? DurationInMonths,
    string ServiceType = MembershipPlanServiceTypes.Gym);

public sealed record MembershipPlanDeleteResponse(bool Deactivated, string Message);
