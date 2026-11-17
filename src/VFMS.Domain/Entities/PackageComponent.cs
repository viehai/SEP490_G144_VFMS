using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Defines a single service entitlement (GYM / YOGA / PT) within a <see cref="Package"/>.
/// Supports single-service packages and multi-service combos.
/// </summary>
public class PackageComponent : BaseEntity
{
    public int PackageId { get; set; }
    public ComponentType ComponentType { get; set; }
    public int? DurationDays { get; set; }
    public int? DurationMonths { get; set; }
    public int? SessionCount { get; set; }
    public int? YogaClassTypeId { get; set; }
    public TrainerLevel? ApplicableTrainerLevel { get; set; }
    public int? PTFreezeMinimumDays { get; set; }
    public int? PTFreezeMaxTotalDays { get; set; }

    // ── Freeze configuration (component-level overrides for GBR-40/41/42) ──
    public bool AllowFreeze { get; set; } = false;
    public int? MaxFreezeDaysPerRequest { get; set; }
    public int? MaxFreezeCount { get; set; }

    // ── Access window (Gym / PT) ──────────────────────────────
    public TimeOnly? AccessStartTime { get; set; }
    public TimeOnly? AccessEndTime { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Package Package { get; set; } = null!;
    public YogaClassType? YogaClassType { get; set; }
    public ICollection<MemberMembership> MemberMemberships { get; set; } = [];
}

