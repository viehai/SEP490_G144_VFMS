namespace VFMS.Contracts.Memberships;

public static class MembershipPlanStatuses
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
}

public sealed class MembershipPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DurationInDays { get; set; }
    public decimal? Price { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class MembershipPlanStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public sealed class MembershipPlanListRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }
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
    int? DurationInMonths);

public sealed record MembershipPlanCatalogItemResponse(
    int Id,
    string Name,
    string? Description,
    int? DurationInDays,
    decimal Price,
    string Status,
    int? DurationInMonths);

public sealed record MembershipPlanDeleteResponse(bool Deactivated, string Message);
