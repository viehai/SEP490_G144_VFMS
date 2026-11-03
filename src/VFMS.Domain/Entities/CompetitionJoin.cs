using VFMS.Domain.Common;
using VFMS.Domain.Enums;

namespace VFMS.Domain.Entities;

/// <summary>
/// Member's registration for a <see cref="Competition"/> (UC-60–UC-66).
/// Staff records preliminary results; Manager confirms final results.
/// </summary>
public class CompetitionJoin : BaseEntity
{
    public int CompetitionId { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public CompetitionJoinStatus Status { get; set; } = CompetitionJoinStatus.Registered;
    public decimal? Score { get; set; }
    public int? Rank { get; set; }
    public string? Note { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedByUserId { get; set; }

    // ── Navigation Properties ──────────────────────────────────
    public Competition Competition { get; set; } = null!;
    public ApplicationUser Member { get; set; } = null!;
    public ApplicationUser? ConfirmedByUser { get; set; }
}

