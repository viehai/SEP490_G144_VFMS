using VFMS.Domain.Common;

namespace VFMS.Domain.Entities;

/// <summary>
/// Represents the physical gym location.
/// </summary>
public class Gym : BaseEntity
{
    public const int DefaultRenewalAdvanceDays = 30;
    public const int DefaultRenewalGraceDays = 60;

    public string GymName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
    public bool IsAcceptingMembers { get; set; } = true;
    public int RenewalAdvanceDays { get; set; } = DefaultRenewalAdvanceDays;
    public int RenewalGraceDays { get; set; } = DefaultRenewalGraceDays;

    // ── Navigation Properties ──────────────────────────────────
    public ICollection<ApplicationUser> Users { get; set; } = [];
    public ICollection<YogaStudio> YogaStudios { get; set; } = [];
    public ICollection<LockerZone> LockerZones { get; set; } = [];
    public ICollection<Equipment> Equipments { get; set; } = [];
    public ICollection<GymAnnouncement> Announcements { get; set; } = [];
}

