using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A PT's declared availability slot for one-on-one sessions.
/// Supports atomic locking to prevent double-booking (GBR-09).
/// </summary>
public class PTAvailability : BaseEntity
{
    public string PTUserId { get; set; } = string.Empty;
    public DateOnly AvailableDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public SlotStatus SlotStatus { get; set; } = SlotStatus.Available;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser PTUser { get; set; } = null!;
    public ICollection<PTSession> PTSessions { get; set; } = [];
}

