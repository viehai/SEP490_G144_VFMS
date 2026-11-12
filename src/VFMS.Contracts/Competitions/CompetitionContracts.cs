namespace VFMS.Contracts.Competitions;

// ── Competitions (UC-60 Manage Competitions, Manager) ──────────

/// <summary>
/// Status: Draft, Published, Ongoing, Completed, Cancelled.
/// A competition is created as "Draft" and published by the Manager.
/// </summary>
public sealed record CompetitionRequest(
    string Name,
    DateTime RegistrationStart,
    DateTime RegistrationEnd,
    DateTime StartDate,
    DateTime EndDate,
    string? Description = null,
    string? Rules = null,
    int? MaxParticipants = null,
    string? ImageUrl = null,
    IReadOnlyCollection<CompetitionPrizeRequest>? Prizes = null
);

/// <summary>Status filter for the competition list. Empty means every status.</summary>
public sealed record CompetitionListRequest(
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record CompetitionResponse(
    int Id,
    string Name,
    string? Description,
    string? Rules,
    DateTime RegistrationStart,
    DateTime RegistrationEnd,
    DateTime StartDate,
    DateTime EndDate,
    int? MaxParticipants,
    string Status,
    string? ImageUrl,
    string CreatedByUserId,
    string? CreatedByName,
    int ParticipantCount,
    IReadOnlyCollection<CompetitionPrizeResponse> Prizes,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// ── Prizes (UC-60) ─────────────────────────────────────────────

/// <summary>A prize for a rank. The voucher reward is optional and must be an Active voucher.</summary>
public sealed record CompetitionPrizeRequest(
    string PrizeName,
    int Rank,
    string? Description = null,
    int? VoucherId = null
);

public sealed record CompetitionPrizeResponse(
    int Id,
    int CompetitionId,
    string PrizeName,
    int Rank,
    string? Description,
    int? VoucherId,
    string? VoucherCode
);

// ── Registration & Participants (UC-57, UC-60, UC-61) ─────────

/// <summary>
/// Status filter for the participant list. Empty means every status.
/// </summary>
public sealed record CompetitionParticipantListRequest(
    int? CompetitionId = null,
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record CompetitionParticipantResponse(
    int Id,
    int CompetitionId,
    string CompetitionName,
    string MemberUserId,
    string MemberName,
    string MemberEmail,
    DateTime RegisteredAt,
    string Status,
    decimal? Score,
    int? Rank,
    string? Note,
    DateTime? ConfirmedAt,
    string? ConfirmedByUserId,
    string? ConfirmedByName
);

// ── Results (UC-61 Manage Competition Results, UC-62 Review and Confirm) ──

/// <summary>
/// The preliminary score recorded by Staff for one participant.
/// </summary>
public sealed record RecordCompetitionResultRequest(
    decimal? Score = null,
    int? Rank = null,
    string? Note = null
);

/// <summary>
/// The final result confirmed by the Manager, optionally granting the prize voucher.
/// </summary>
public sealed record ConfirmCompetitionResultRequest(
    int JoinId,
    decimal? Score = null,
    int? Rank = null,
    string? Note = null,
    bool GrantPrizeVoucher = false
);

public sealed record CompetitionLeaderboardEntry(
    int JoinId,
    int Rank,
    string MemberUserId,
    string MemberName,
    decimal? Score,
    string Status,
    string? PrizeName,
    string? VoucherCode,
    string? Note = null
);
