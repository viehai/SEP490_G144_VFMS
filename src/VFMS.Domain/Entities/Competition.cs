using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Competition configured by a Manager (UC-60–UC-66).
/// A Manager publishes, cancels, or completes a competition; completion is manual after the start time.
/// </summary>
public class Competition : BaseEntity
{
    public string CompetitionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Rules { get; set; }
    public DateTime RegistrationStart { get; set; }
    public DateTime RegistrationEnd { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? MaxParticipants { get; set; }
    public CompetitionStatus Status { get; set; } = CompetitionStatus.Draft;
    public string? ImageUrl { get; set; }
    public string? CreatedByUserId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public ApplicationUser? CreatedByUser { get; set; }
    public ICollection<CompetitionJoin> Participants { get; set; } = [];
    public ICollection<CompetitionPrize> Prizes { get; set; } = [];
}

