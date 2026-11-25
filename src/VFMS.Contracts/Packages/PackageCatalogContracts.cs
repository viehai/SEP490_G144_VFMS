using FluentValidation;

namespace VFMS.Contracts.Packages;

/// <summary>
/// Query parameters cho public package catalog endpoint.
/// </summary>
public sealed class PackageCatalogQuery
{
    /// <summary>Loại component: "gym", "yoga", hoặc "pt" (case-insensitive).</summary>
    public string Type { get; set; } = string.Empty;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Response DTO dùng chung cho cả GYM / YOGA / PT trong public catalog.
/// </summary>
public sealed record PackageCatalogItemResponse(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    string Status,          // "ACTIVE"
    string ComponentType,   // "GYM" | "YOGA" | "PT"

    // --- Duration (GYM & YOGA) ---
    int? DurationDays,
    int? DurationMonths,

    // --- Sessions (YOGA & PT) ---
    int? SessionCount,

    // --- Yoga-specific ---
    int? YogaClassTypeId,
    string? YogaClassTypeName,

    // --- PT-specific ---
    string? TrainerLevel,
    int? PTFreezeMinimumDays,
    int? PTFreezeMaxTotalDays,

    // --- Freeze (GYM & YOGA) ---
    bool AllowFreeze,
    int? MaxFreezeDaysPerRequest,
    int? MaxFreezeCount,

    // --- Access window (GYM) ---
    TimeOnly? AccessStartTime,
    TimeOnly? AccessEndTime
)
{
    public bool IsSingleService { get; init; } = true;
    public string? TargetAudience { get; init; }
    public int? TotalFreezeAllowanceDays { get; init; }
}

public sealed record PackageDetailResponse(
    PackageCatalogItemResponse Package,
    IReadOnlyList<PackageCatalogItemResponse> Recommendations);

public sealed class PackageCatalogQueryValidator : AbstractValidator<PackageCatalogQuery>
{
    private static readonly HashSet<string> ValidTypes =
        new(StringComparer.OrdinalIgnoreCase) { "gym", "yoga", "pt" };

    public PackageCatalogQueryValidator()
    {
        RuleFor(x => x.Type)
            .Must(t => !string.IsNullOrWhiteSpace(t) && ValidTypes.Contains(t.Trim()))
            .WithMessage("Type must be one of: gym, yoga, pt.");

        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
