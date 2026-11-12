using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Competitions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>
/// Module 6 – Competitions.
/// UC-56 View Competitions (Guest, Customer, Member, Staff, Manager).
/// UC-57 Competition Registration (Member) – only for a Published competition inside its registration window.
/// UC-58 View Competition History (Member).
/// UC-59 Manage Competitions (Manager) – Draft → Published → Ongoing → Completed (JOB-05).
/// UC-60 Manage Competition Participants (Staff, Manager).
/// UC-61 Manage Competition Results (Staff) – records the preliminary score.
/// UC-62 Review and Confirm Competition Results (Manager) – confirms the final result and grants the prize voucher.
/// </summary>
public sealed class CompetitionService : ICompetitionService
{
    private const string Entity = nameof(Competition);
    private const string JoinEntity = nameof(CompetitionJoin);
    private static readonly TimeSpan GymOffset = TimeSpan.FromHours(7);

    private readonly ICompetitionRepository _repository;

    public CompetitionService(ICompetitionRepository repository)
    {
        _repository = repository;
    }

    // ── Read (UC-56) ─────────────────────────────────────────────

    public async Task<Result<PagedResponse<CompetitionResponse>>> GetCompetitionsAsync(
        CompetitionListRequest request, CancellationToken cancellationToken = default)
    {
        CompetitionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EquipmentService.TryParse(request.Status, out CompetitionStatus parsed))
                return Result<PagedResponse<CompetitionResponse>>.Failure("Status filter is invalid.", "COMPETITION_INVALID_STATUS");
            status = parsed;
        }

        var (items, total) = await _repository.SearchCompetitionsAsync(
            status, EquipmentService.Clean(request.Search), request.Page, request.PageSize, cancellationToken);

        // Project each competition with its real participant count.
        // NOTE: a method group must not be passed directly to Select, otherwise it binds to
        // Select<TSource, TResult>(source, Func<TSource, int, TResult>) and the second argument
        // (the item index) is used as the participant count.
        var responses = new CompetitionResponse[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var count = await _repository.CountActiveParticipantsAsync(items[index].Id, cancellationToken);
            responses[index] = ToResponse(items[index], count);
        }

        return Result<PagedResponse<CompetitionResponse>>.Success(new PagedResponse<CompetitionResponse>(
            request.Page, request.PageSize, total, responses));
    }

    public async Task<Result<CompetitionResponse>> GetCompetitionAsync(int id, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionWithDetailsAsync(id, cancellationToken);
        if (competition is null)
            return NotFound<CompetitionResponse>();
        var participants = await _repository.CountActiveParticipantsAsync(id, cancellationToken);
        return Result<CompetitionResponse>.Success(ToResponse(competition, participants));
    }

    public async Task<Result<IReadOnlyList<CompetitionLeaderboardEntry>>> GetLeaderboardAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionWithDetailsAsync(id, cancellationToken);
        if (competition is null)
            return Result<IReadOnlyList<CompetitionLeaderboardEntry>>.Failure(
                "Competition was not found.", "COMPETITION_NOT_FOUND", 404);

        var (_, total) = await _repository.SearchParticipantsAsync(id, null, null, 1, 100, cancellationToken);
        var participants = await _repository.SearchParticipantsAsync(id, null, null, 1, Math.Max(total, 1), cancellationToken);
        var entries = participants.Items
            .Where(j => j.Status != CompetitionJoinStatus.Disqualified)
            .Select((j, index) => new
            {
                Join = j,
                Rank = j.Rank ?? index + 1
            })
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.Join.Score ?? decimal.MinValue)
            .Select(x =>
            {
                var prize = competition.Prizes.FirstOrDefault(p => p.Rank == x.Rank);
                return new CompetitionLeaderboardEntry(
                    x.Join.Id, x.Rank, x.Join.MemberUserId,
                    x.Join.Member?.FullName ?? string.Empty, x.Join.Score, x.Join.Status.ToString(),
                    prize?.PrizeName, prize?.Voucher?.Code, x.Join.Note);
            })
            .ToArray();
        return Result<IReadOnlyList<CompetitionLeaderboardEntry>>.Success(entries);
    }

    // ── Member actions (UC-57, UC-58) ────────────────────────────

    public async Task<Result<CompetitionParticipantResponse>> RegisterAsync(
        int competitionId, string memberUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionAsync(competitionId, cancellationToken);
        if (competition is null)
            return NotFound<CompetitionParticipantResponse>();

        // Only a published competition accepts registrations.
        if (competition.Status != CompetitionStatus.Published)
            return Result<CompetitionParticipantResponse>.Failure(
                $"This competition is {competition.Status} and does not accept registrations.",
                "COMPETITION_NOT_OPEN", 409);

        var now = DateTime.UtcNow;
        if (now < competition.RegistrationStart)
            return Result<CompetitionParticipantResponse>.Failure(
                $"Registration opens at {competition.RegistrationStart:yyyy-MM-dd HH:mm} UTC.",
                "COMPETITION_REGISTRATION_NOT_STARTED", 409);
        if (now > competition.RegistrationEnd)
            return Result<CompetitionParticipantResponse>.Failure(
                $"Registration closed at {competition.RegistrationEnd:yyyy-MM-dd HH:mm} UTC.",
                "COMPETITION_REGISTRATION_CLOSED", 409);

        // One registration per member per competition.
        if (await _repository.IsParticipantAsync(competitionId, memberUserId, cancellationToken))
            return Result<CompetitionParticipantResponse>.Failure(
                "You are already registered for this competition.", "COMPETITION_ALREADY_REGISTERED", 409);

        // The capacity counts every participant that is not disqualified.
        if (competition.MaxParticipants is { } max)
        {
            var count = await _repository.CountActiveParticipantsAsync(competitionId, cancellationToken);
            if (count >= max)
                return Result<CompetitionParticipantResponse>.Failure(
                    $"This competition already has its maximum of {max} participants.",
                    "COMPETITION_FULL", 409);
        }

        var join = new CompetitionJoin
        {
            CompetitionId = competitionId,
            MemberUserId = memberUserId,
            RegisteredAt = now,
            Status = CompetitionJoinStatus.Registered
        };
        var audit = AuditedWriter.Audit(memberUserId, AuditActionType.Create, JoinEntity, ipAddress,
            newValue: $"Competition={competition.CompetitionName}");
        await AuditedWriter.SaveNewAsync(_repository, join, audit, null, cancellationToken);

        var saved = await _repository.GetParticipantAsync(join.Id, cancellationToken) ?? join;
        return Result<CompetitionParticipantResponse>.Success(ToParticipant(saved), 201);
    }

    public Task<Result<PagedResponse<CompetitionParticipantResponse>>> GetMyHistoryAsync(
        CompetitionParticipantListRequest request, string memberUserId, CancellationToken cancellationToken = default) =>
        SearchParticipantsAsync(request, memberUserId, null, cancellationToken);

    // ── Manager actions (UC-59) ──────────────────────────────────

    public async Task<Result<CompetitionResponse>> CreateAsync(
        CompetitionRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await _repository.CompetitionNameExistsAsync(name, null, cancellationToken))
            return Result<CompetitionResponse>.Failure(
                "A competition with this name already exists.", "COMPETITION_NAME_EXISTS", 409);

        if (!await ValidatePrizesAsync(request.Prizes, cancellationToken))
            return Result<CompetitionResponse>.Failure(
                "One of the prize vouchers is not available. Choose an Active voucher or remove the prize reward.",
                "COMPETITION_PRIZE_VOUCHER_INVALID");

        var competition = new Competition
        {
            CompetitionName = name,
            Description = EquipmentService.Clean(request.Description),
            Rules = EquipmentService.Clean(request.Rules),
            RegistrationStart = request.RegistrationStart,
            RegistrationEnd = request.RegistrationEnd,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            MaxParticipants = request.MaxParticipants,
            Status = CompetitionStatus.Draft,
            ImageUrl = EquipmentService.Clean(request.ImageUrl),
            CreatedByUserId = managerUserId
        };
        foreach (var prize in request.Prizes ?? [])
            competition.Prizes.Add(new CompetitionPrize
            {
                CompetitionId = competition.Id,
                PrizeName = prize.PrizeName.Trim(),
                Rank = prize.Rank,
                Description = EquipmentService.Clean(prize.Description),
                VoucherId = prize.VoucherId
            });

        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Create, Entity, ipAddress,
            newValue: $"Name={name}; Start={competition.StartDate:yyyy-MM-dd}; End={competition.EndDate:yyyy-MM-dd}");
        await AuditedWriter.SaveNewAsync(_repository, competition, audit, null, cancellationToken);

        var saved = await _repository.GetCompetitionWithDetailsAsync(competition.Id, cancellationToken) ?? competition;
        return Result<CompetitionResponse>.Success(ToResponse(saved, 0), 201);
    }

    public async Task<Result<CompetitionResponse>> UpdateAsync(
        int id, CompetitionRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionAsync(id, cancellationToken);
        if (competition is null)
            return NotFound<CompetitionResponse>();

        // A competition that already started keeps its published data (JOB-05 advances the status by itself).
        if (competition.Status is CompetitionStatus.Ongoing or CompetitionStatus.Completed)
            return Result<CompetitionResponse>.Failure(
                "A running or finished competition cannot be edited.", "COMPETITION_NOT_EDITABLE", 409);

        var name = request.Name.Trim();
        if (await _repository.CompetitionNameExistsAsync(name, id, cancellationToken))
            return Result<CompetitionResponse>.Failure(
                "A competition with this name already exists.", "COMPETITION_NAME_EXISTS", 409);

        if (!await ValidatePrizesAsync(request.Prizes, cancellationToken))
            return Result<CompetitionResponse>.Failure(
                "One of the prize vouchers is not available. Choose an Active voucher or remove the prize reward.",
                "COMPETITION_PRIZE_VOUCHER_INVALID");

        var before = $"Name={competition.CompetitionName}; Start={competition.StartDate:yyyy-MM-dd}; End={competition.EndDate:yyyy-MM-dd}";
        competition.CompetitionName = name;
        competition.Description = EquipmentService.Clean(request.Description);
        competition.Rules = EquipmentService.Clean(request.Rules);
        competition.RegistrationStart = request.RegistrationStart;
        competition.RegistrationEnd = request.RegistrationEnd;
        competition.StartDate = request.StartDate;
        competition.EndDate = request.EndDate;
        competition.MaxParticipants = request.MaxParticipants;
        competition.ImageUrl = EquipmentService.Clean(request.ImageUrl);

        // Replace the prize list with the submitted one.
        competition.Prizes.Clear();
        foreach (var prize in request.Prizes ?? [])
            competition.Prizes.Add(new CompetitionPrize
            {
                CompetitionId = competition.Id,
                PrizeName = prize.PrizeName.Trim(),
                Rank = prize.Rank,
                Description = EquipmentService.Clean(prize.Description),
                VoucherId = prize.VoucherId
            });

        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress, before,
            $"Name={name}; Start={competition.StartDate:yyyy-MM-dd}; End={competition.EndDate:yyyy-MM-dd}");
        await AuditedWriter.SaveChangedAsync(_repository, competition, audit, null, cancellationToken);

        var saved = await _repository.GetCompetitionWithDetailsAsync(id, cancellationToken) ?? competition;
        var participants = await _repository.CountActiveParticipantsAsync(id, cancellationToken);
        return Result<CompetitionResponse>.Success(ToResponse(saved, participants));
    }

    public async Task<Result<CompetitionResponse>> PublishAsync(
        int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionAsync(id, cancellationToken);
        if (competition is null)
            return NotFound<CompetitionResponse>();
        if (competition.Status != CompetitionStatus.Draft)
            return Result<CompetitionResponse>.Failure(
                $"Only a Draft competition can be published. Current status: {competition.Status}.",
                "COMPETITION_INVALID_STATE", 409);
        if (competition.RegistrationEnd < DateTime.UtcNow)
            return Result<CompetitionResponse>.Failure(
                "The registration period already ended. Adjust the dates before publishing.",
                "COMPETITION_REGISTRATION_ENDED", 409);

        competition.Status = CompetitionStatus.Published;
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={CompetitionStatus.Draft}", $"Status={CompetitionStatus.Published}");
        await AuditedWriter.SaveChangedAsync(_repository, competition, audit, null, cancellationToken);

        var saved = await _repository.GetCompetitionWithDetailsAsync(id, cancellationToken) ?? competition;
        var participants = await _repository.CountActiveParticipantsAsync(id, cancellationToken);
        return Result<CompetitionResponse>.Success(ToResponse(saved, participants));
    }

    public async Task<Result<CompetitionResponse>> CancelAsync(
        int id, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionAsync(id, cancellationToken);
        if (competition is null)
            return NotFound<CompetitionResponse>();
        if (competition.Status is CompetitionStatus.Completed or CompetitionStatus.Cancelled)
            return Result<CompetitionResponse>.Failure(
                $"A {competition.Status} competition cannot be cancelled.", "COMPETITION_INVALID_STATE", 409);
        if (competition.Status == CompetitionStatus.Ongoing)
            return Result<CompetitionResponse>.Failure(
                "A running competition cannot be cancelled.", "COMPETITION_ALREADY_STARTED", 409);

        var previousStatus = competition.Status;
        competition.Status = CompetitionStatus.Cancelled;
        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            $"Status={previousStatus}", $"Status={CompetitionStatus.Cancelled}");
        await AuditedWriter.SaveChangedAsync(_repository, competition, audit, null, cancellationToken);

        var saved = await _repository.GetCompetitionWithDetailsAsync(id, cancellationToken) ?? competition;
        var participants = await _repository.CountActiveParticipantsAsync(id, cancellationToken);
        return Result<CompetitionResponse>.Success(ToResponse(saved, participants));
    }

    // ── Participants (UC-60) ─────────────────────────────────────

    public Task<Result<PagedResponse<CompetitionParticipantResponse>>> GetParticipantsAsync(
        CompetitionParticipantListRequest request, CancellationToken cancellationToken = default) =>
        SearchParticipantsAsync(request, null, null, cancellationToken);

    public async Task<Result<CompetitionParticipantResponse>> DisqualifyAsync(
        int joinId, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var join = await _repository.GetParticipantAsync(joinId, cancellationToken);
        if (join is null)
            return NotFound<CompetitionParticipantResponse>();
        if (join.Status == CompetitionJoinStatus.Disqualified)
            return Result<CompetitionParticipantResponse>.Failure(
                "This participant is already disqualified.", "COMPETITION_PARTICIPANT_INVALID_STATE", 409);
        if (join.Status == CompetitionJoinStatus.Completed)
            return Result<CompetitionParticipantResponse>.Failure(
                "A participant with a confirmed result cannot be disqualified.", "COMPETITION_PARTICIPANT_INVALID_STATE", 409);

        join.Status = CompetitionJoinStatus.Disqualified;
        var audit = AuditedWriter.Audit(staffUserId, AuditActionType.Update, JoinEntity, ipAddress,
            $"Status={CompetitionJoinStatus.Registered}", $"Status={CompetitionJoinStatus.Disqualified}");
        await AuditedWriter.SaveChangedAsync(_repository, join, audit, null, cancellationToken);
        return await ReloadParticipant(joinId, cancellationToken);
    }

    public async Task<Result<CompetitionParticipantResponse>> ReinstateAsync(
        int joinId, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var join = await _repository.GetParticipantAsync(joinId, cancellationToken);
        if (join is null)
            return NotFound<CompetitionParticipantResponse>();
        if (join.Status != CompetitionJoinStatus.Disqualified)
            return Result<CompetitionParticipantResponse>.Failure(
                "This participant is not disqualified.", "COMPETITION_PARTICIPANT_INVALID_STATE", 409);

        join.Status = CompetitionJoinStatus.Registered;
        var audit = AuditedWriter.Audit(staffUserId, AuditActionType.Update, JoinEntity, ipAddress,
            $"Status={CompetitionJoinStatus.Disqualified}", $"Status={CompetitionJoinStatus.Registered}");
        await AuditedWriter.SaveChangedAsync(_repository, join, audit, null, cancellationToken);
        return await ReloadParticipant(joinId, cancellationToken);
    }

    // ── Results (UC-61, UC-62) ───────────────────────────────────

    public async Task<Result<CompetitionParticipantResponse>> RecordResultAsync(
        int joinId, RecordCompetitionResultRequest request, string staffUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var join = await _repository.GetParticipantAsync(joinId, cancellationToken);
        if (join is null)
            return NotFound<CompetitionParticipantResponse>();
        if (join.Status == CompetitionJoinStatus.Disqualified)
            return Result<CompetitionParticipantResponse>.Failure(
                "A disqualified participant cannot receive a result.", "COMPETITION_PARTICIPANT_INVALID_STATE", 409);
        // UC-62: a confirmed result is final for Staff; only the Manager can change it.
        if (join.Status == CompetitionJoinStatus.Completed)
            return Result<CompetitionParticipantResponse>.Failure(
                "This result was already confirmed by the Manager.", "COMPETITION_RESULT_ALREADY_CONFIRMED", 409);

        var before = $"Score={join.Score}; Rank={join.Rank}; Note={join.Note}";
        join.Score = request.Score;
        join.Rank = request.Rank;
        join.Note = EquipmentService.Clean(request.Note);

        var audit = AuditedWriter.Audit(staffUserId, AuditActionType.Update, JoinEntity, ipAddress, before,
            $"Score={join.Score}; Rank={join.Rank}; Note={join.Note}");
        await AuditedWriter.SaveChangedAsync(_repository, join, audit, null, cancellationToken);
        return await ReloadParticipant(joinId, cancellationToken);
    }

    public async Task<Result<CompetitionParticipantResponse>> ConfirmResultAsync(
        ConfirmCompetitionResultRequest request, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var join = await _repository.GetParticipantAsync(request.JoinId, cancellationToken);
        if (join is null)
            return NotFound<CompetitionParticipantResponse>();
        if (join.Status == CompetitionJoinStatus.Disqualified)
            return Result<CompetitionParticipantResponse>.Failure(
                "A disqualified participant cannot have a confirmed result.", "COMPETITION_PARTICIPANT_INVALID_STATE", 409);

        var competition = await _repository.GetCompetitionAsync(join.CompetitionId, cancellationToken);
        if (competition is null)
            return NotFound<CompetitionParticipantResponse>();

        // A result can only be confirmed once the competition has finished.
        if (competition.Status != CompetitionStatus.Completed)
            return Result<CompetitionParticipantResponse>.Failure(
                $"Results can only be confirmed after the competition is completed. Current status: {competition.Status}.",
                "COMPETITION_NOT_COMPLETED", 409);

        var rank = request.Rank ?? join.Rank;
        if (rank is null)
            return Result<CompetitionParticipantResponse>.Failure(
                "A rank is required to confirm the result.", "COMPETITION_RESULT_RANK_REQUIRED");

        var prize = await _repository.GetPrizeForRankAsync(competition.Id, rank.Value, cancellationToken);
        if (request.GrantPrizeVoucher && prize?.VoucherId is null)
            return Result<CompetitionParticipantResponse>.Failure(
                $"Rank {rank} has no prize voucher to grant.", "COMPETITION_PRIZE_NOT_FOUND", 409);

        var before = $"Status={join.Status}; Score={join.Score}; Rank={join.Rank}";
        join.Score = request.Score ?? join.Score;
        join.Rank = rank;
        join.Note = EquipmentService.Clean(request.Note) ?? join.Note;
        join.Status = CompetitionJoinStatus.Completed;
        join.ConfirmedAt = DateTime.UtcNow;
        join.ConfirmedByUserId = managerUserId;

        var notifications = new List<Notification>();
        if (request.GrantPrizeVoucher && prize?.VoucherId is { } voucherId)
        {
            var voucher = await _repository.GetVoucherAsync(voucherId, cancellationToken);
            if (voucher is not null)
            {
                var code = voucher.Code;
                notifications.Add(AuditedWriter.Notify(join.MemberUserId, managerUserId, "Competition prize awarded",
                    $"Congratulations! You won \"{prize.PrizeName}\" in {competition.CompetitionName}. " +
                    $"Use voucher code {code} at checkout.", JoinEntity, NotificationType.CompetitionResult));
            }
        }

        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, JoinEntity, ipAddress, before,
            $"Status={join.Status}; Score={join.Score}; Rank={join.Rank}");
        await AuditedWriter.SaveChangedAsync(_repository, join, audit, notifications, cancellationToken);
        return await ReloadParticipant(request.JoinId, cancellationToken);
    }

    public async Task<Result<int>> ConfirmAllResultsAsync(
        int competitionId, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var competition = await _repository.GetCompetitionAsync(competitionId, cancellationToken);
        if (competition is null)
            return NotFound<int>();
        if (competition.Status != CompetitionStatus.Completed)
            return Result<int>.Failure(
                $"Results can only be confirmed after the competition is completed. Current status: {competition.Status}.",
                "COMPETITION_NOT_COMPLETED", 409);

        var (_, total) = await _repository.SearchParticipantsAsync(competitionId, null, null, 1, 500, cancellationToken);
        var participants = await _repository.SearchParticipantsAsync(competitionId, null, null, 1, Math.Max(total, 1), cancellationToken);

        var confirmed = 0;
        foreach (var join in participants.Items)
        {
            if (join.Status == CompetitionJoinStatus.Completed || join.Status == CompetitionJoinStatus.Disqualified)
                continue;
            if (join.Rank is null)
                continue;
            join.Status = CompetitionJoinStatus.Completed;
            join.ConfirmedAt = DateTime.UtcNow;
            join.ConfirmedByUserId = managerUserId;
            confirmed++;
        }

        if (confirmed == 0)
            return Result<int>.Failure(
                "There is no result with a rank to confirm.", "COMPETITION_NO_RESULT_TO_CONFIRM", 409);

        var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
            newValue: $"Confirmed={confirmed} results");
        await AuditedWriter.SaveChangedAsync(_repository, competition, audit, null, cancellationToken);
        return Result<int>.Success(confirmed);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<Result<PagedResponse<CompetitionParticipantResponse>>> SearchParticipantsAsync(
        CompetitionParticipantListRequest request, string? memberUserId, CompetitionJoinStatus? statusFilter,
        CancellationToken cancellationToken)
    {
        CompetitionJoinStatus? status = statusFilter;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EquipmentService.TryParse(request.Status, out CompetitionJoinStatus parsed))
                return Result<PagedResponse<CompetitionParticipantResponse>>.Failure(
                    "Status filter is invalid.", "COMPETITION_INVALID_STATUS");
            status = parsed;
        }

        var search = EquipmentService.Clean(request.Search);
        if (memberUserId is not null)
        {
            // A member sees only their own history (UC-58), so the search text does not apply there.
            var (ownItems, ownTotal) = await _repository.SearchMemberRegistrationsAsync(
                memberUserId, request.CompetitionId, status, request.Page, request.PageSize, cancellationToken);
            return Result<PagedResponse<CompetitionParticipantResponse>>.Success(new PagedResponse<CompetitionParticipantResponse>(
                request.Page, request.PageSize, ownTotal, ownItems.Select(ToParticipant).ToArray()));
        }

        var (items, total) = await _repository.SearchParticipantsAsync(
            request.CompetitionId, status, search, request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<CompetitionParticipantResponse>>.Success(new PagedResponse<CompetitionParticipantResponse>(
            request.Page, request.PageSize, total, items.Select(ToParticipant).ToArray()));
    }

    private async Task<bool> ValidatePrizesAsync(IReadOnlyCollection<CompetitionPrizeRequest>? prizes, CancellationToken cancellationToken)
    {
        if (prizes is null || prizes.Count == 0)
            return true;
        foreach (var prize in prizes.Where(p => p.VoucherId.HasValue))
        {
            var voucher = await _repository.GetVoucherAsync(prize.VoucherId!.Value, cancellationToken);
            if (voucher is null || voucher.VoucherStatus != VoucherStatus.Active)
                return false;
        }
        return true;
    }

    private async Task<Result<CompetitionParticipantResponse>> ReloadParticipant(int joinId, CancellationToken cancellationToken)
    {
        var join = await _repository.GetParticipantAsync(joinId, cancellationToken);
        return join is null
            ? NotFound<CompetitionParticipantResponse>()
            : Result<CompetitionParticipantResponse>.Success(ToParticipant(join));
    }

    private static CompetitionResponse ToResponse(Competition c, int participantCount) =>
        new(c.Id, c.CompetitionName, c.Description, c.Rules, c.RegistrationStart, c.RegistrationEnd,
            c.StartDate, c.EndDate, c.MaxParticipants, c.Status.ToString(), c.ImageUrl,
            c.CreatedByUserId, c.CreatedByUser?.FullName, participantCount,
            c.Prizes.OrderBy(p => p.Rank).Select(ToPrize).ToArray(), c.CreatedAt, c.UpdatedAt);

    private static CompetitionPrizeResponse ToPrize(CompetitionPrize p) =>
        new(p.Id, p.CompetitionId, p.PrizeName, p.Rank, p.Description, p.VoucherId, p.Voucher?.Code);

    private static CompetitionParticipantResponse ToParticipant(CompetitionJoin j) =>
        new(j.Id, j.CompetitionId, j.Competition?.CompetitionName ?? string.Empty, j.MemberUserId,
            j.Member?.FullName ?? string.Empty, j.Member?.Email ?? string.Empty, j.RegisteredAt,
            j.Status.ToString(), j.Score, j.Rank, j.Note, j.ConfirmedAt, j.ConfirmedByUserId,
            j.ConfirmedByUser?.FullName);

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure("Competition was not found.", "COMPETITION_NOT_FOUND", 404);
}
