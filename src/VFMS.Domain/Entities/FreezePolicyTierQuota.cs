using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>A cumulative freeze allowance for a package term under the global policy (GBR-41).</summary>
public sealed class FreezePolicyTierQuota
{
    public int Duration { get; set; }
    public FreezeTierUnit Unit { get; set; }
    public int MaximumCumulativeFreezeDays { get; set; }
}
