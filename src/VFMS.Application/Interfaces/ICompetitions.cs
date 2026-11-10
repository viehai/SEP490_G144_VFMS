using VFMS.Contracts.Common;
using VFMS.Contracts.Competitions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

/// <summary>UC-56/57/58/59/60/61/62 – Competitions module (Module 6).</summary>
public interface ICompetitionRepository : IAuditedRepository
{
    // ── Competitions ─────────────────────────────────────────────

    Task<(IReadOnlyList<Competition> Items, int TotalCount)> SearchCompetitionsAsync(
        CompetitionStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Competition?> GetCompetitionAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Competition with its prizes and participant count loaded, for the response projection.</summary>
    Task<Competition?> GetCompetitionWithDetailsAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> CompetitionNameExistsAsync(string name, int? excludingId, CancellationToken cancellationToken = default);

    /// <summary>Number of participants that count towards the capacity (not disqualified).</summary>
    Task<int> CountActiveParticipantsAsync(int competitionId, CancellationToken cancellationToken = default);

    // ── Participants ─────────────────────────────────────────────

    Task<(IReadOnlyList<CompetitionJoin> Items, int TotalCount)> SearchParticipantsAsync(
        int? competitionId, CompetitionJoinStatus? status, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<CompetitionJoin?> GetParticipantAsync(int id, CancellationToken cancellationToken = default);

    Task<CompetitionJoin?> GetParticipantByMemberAsync(int competitionId, string memberUserId, CancellationToken cancellationToken = default);

    /// <summary>Every registration of one member, optionally narrowed to one competition.</summary>
    Task<(IReadOnlyList<CompetitionJoin> Items, int TotalCount)> SearchMemberRegistrationsAsync(
        string memberUserId, int? competitionId, CompetitionJoinStatus? status,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<bool> IsParticipantAsync(int competitionId, string memberUserId, CancellationToken cancellationToken = default);

    // ── Prizes ───────────────────────────────────────────────────

    Task<CompetitionPrize?> GetPrizeForRankAsync(int competitionId, int rank, CancellationToken cancellationToken = default);

    Task<Voucher?> GetVoucherAsync(int voucherId, CancellationToken cancellationToken = default);
}
