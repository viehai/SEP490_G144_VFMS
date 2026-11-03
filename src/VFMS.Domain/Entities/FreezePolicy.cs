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
    public int DefaultMaxFreezeDays { get; set; }
    public int DefaultMaxFreezeCount { get; set; }
    public PolicyStatus PolicyStatus { get; set; } = PolicyStatus.Active;
    public bool IsGlobal { get; set; } = true;
}

