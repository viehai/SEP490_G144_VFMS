using VFMS.Contracts.Common;
using VFMS.Contracts.Competitions;

namespace VFMS.Application.Interfaces;

/// <summary>
/// UC-56 View Competitions (Guest, Customer, Member, Staff, Manager).
/// UC-57 Competition Registration (Member).
/// UC-58 View Competition History (Member).
/// UC-59 Manage Competitions (Manager).
/// UC-60 Manage Competition Participants (Staff, Manager).
/// UC-61 Manage Competition Results (Staff).
/// UC-62 Review and Confirm Competition Results (Manager).
/// </summary>
public interface ICompetitionService
{
    // ── Read (UC-56) ─────────────────────────────────────────────

    Task<Result<PagedResponse<CompetitionResponse>>> GetCompetitionsAsync(
        CompetitionListRequest request, CancellationToken cancellationToken = default);

    Task<Result<CompetitionResponse>> GetCompetitionAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The leaderboard of a competition: participants ordered by rank, then score.</summary>
    Task<Result<IReadOnlyList<CompetitionLeaderboardEntry>>> GetLeaderboardAsync(
        int id, CancellationToken cancellationToken = default);

    // ── Member actions (UC-57, UC-58) ────────────────────────────

    Task<Result<CompetitionParticipantResponse>> RegisterAsync(
        int competitionId, string memberUserId, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<CompetitionParticipantResponse>>> GetMyHistoryAsync(
        CompetitionParticipantListRequest request, string memberUserId, CancellationToken cancellationToken = default);

    // ── Manager actions (UC-59) ──────────────────────────────────

    Task<Result<CompetitionResponse>> CreateAsync(
        CompetitionRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<CompetitionResponse>> UpdateAsync(
        int id, CompetitionRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Publishes a Draft competition so members can register.</summary>
    Task<Result<CompetitionResponse>> PublishAsync(
        int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Cancels a competition that has not started yet.</summary>
    Task<Result<CompetitionResponse>> CancelAsync(
        int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);

    // ── Participants (UC-60) ─────────────────────────────────────

    Task<Result<PagedResponse<CompetitionParticipantResponse>>> GetParticipantsAsync(
        CompetitionParticipantListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Staff disqualifies a participant so it no longer counts towards the capacity.</summary>
    Task<Result<CompetitionParticipantResponse>> DisqualifyAsync(
        int joinId, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Staff reinstates a disqualified participant.</summary>
    Task<Result<CompetitionParticipantResponse>> ReinstateAsync(
        int joinId, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);

    // ── Results (UC-61, UC-62) ───────────────────────────────────

    /// <summary>Staff records the preliminary score and rank of a participant.</summary>
    Task<Result<CompetitionParticipantResponse>> RecordResultAsync(
        int joinId, RecordCompetitionResultRequest request, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Manager reviews and confirms the final result, optionally granting the prize voucher.</summary>
    Task<Result<CompetitionParticipantResponse>> ConfirmResultAsync(
        ConfirmCompetitionResultRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Manager confirms every result of a competition at once.</summary>
    Task<Result<int>> ConfirmAllResultsAsync(
        int competitionId, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default);
}
