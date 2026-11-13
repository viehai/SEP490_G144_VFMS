using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Global freeze policy defaults. Component-level overrides live in <see cref="PackageComponent"/>.
/// </summary>
public class FreezePolicy : BaseEntity
{
    public string PolicyName { get; set; } = string.Empty;
    /// <summary>Minimum paid days before a member may request a freeze (GBR-42).</summary>
    public int EligibilityPaidDays { get; set; }
    /// <summary>Minimum advance notice days required for a freeze request (GBR-42).</summary>
    public int MinimumRequestDays { get; set; }
    /// <summary>Minimum consecutive days per request. Null for legacy policies awaiting configuration.</summary>
    public int? MinFreezeDaysPerRequest { get; set; }
    /// <summary>Global maximum consecutive days per request (GBR-40).</summary>
    public int DefaultMaxFreezeDays { get; set; }
    public int DefaultMaxFreezeCount { get; set; }
    public PolicyStatus PolicyStatus { get; set; } = PolicyStatus.Active;
    public bool IsGlobal { get; set; } = true;

    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<FreezePolicyTierQuota> TierQuotas { get; set; } = [];
}

