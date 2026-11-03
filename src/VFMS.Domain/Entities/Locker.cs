using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Individual locker unit within a <see cref="LockerZone"/>.
/// Each Member may hold at most 1 active locker at a time (GBR-18).
/// </summary>
public class Locker : BaseEntity
{
    public int LockerZoneId { get; set; }
    public int? LockerPricingPolicyId { get; set; }
    public string LockerNumber { get; set; } = string.Empty;
    public LockerType LockerType { get; set; }
    public LockerStatus LockerStatus { get; set; } = LockerStatus.Available;
    public string? AssignedPinCode { get; set; }
    public KeyOrPinType? KeyOrPinType { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public LockerZone LockerZone { get; set; } = null!;
    public LockerPricingPolicy? LockerPricingPolicy { get; set; }
    public ICollection<LockerRentalContract> RentalContracts { get; set; } = [];
    public ICollection<LockerIncidentLog> IncidentLogs { get; set; } = [];
}

