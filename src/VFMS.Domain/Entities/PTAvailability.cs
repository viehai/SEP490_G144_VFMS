using System.ComponentModel.DataAnnotations.Schema;
using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// A weekly template slot for one-on-one sessions, or a legacy dated slot.
/// Holds and confirmed appointments are tracked on PTSession.
/// </summary>
public class PTAvailability : BaseEntity
{
    public string PTUserId { get; set; } = string.Empty;
    // Only explicitly published rows repeat. Legacy dated slots have no day of week.
    public DayOfWeek? DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    [NotMapped]
    public TimeOnly EndTime => StartTime.AddHours(1);
    public SlotStatus SlotStatus { get; set; } = SlotStatus.Available;

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser PTUser { get; set; } = null!;
    public ICollection<PTSession> PTSessions { get; set; } = [];
}

