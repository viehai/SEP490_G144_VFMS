using VFMS.Contracts.Common;
using VFMS.Contracts.Competitions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed partial class CompetitionService
{
    public async Task<Result<IReadOnlyList<CompetitionRewardVoucher>>> RewardOptionsAsync(DateTime? competitionEnd, int? competitionId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var at = competitionEnd is { } end && end > now ? end : now;
        var options = new List<CompetitionRewardVoucher>();
        foreach (var voucher in await _repository.RewardVoucherOptionsAsync(at, ct))
        {
            int? available = voucher.MaxRedemptions.HasValue
                ? voucher.MaxRedemptions - await _repository.CountCommittedAsync(voucher.Id, competitionId, ct)
                : null;
            if (available is null or > 0)
                options.Add(RewardDetails(voucher, available));
        }
        return Result<IReadOnlyList<CompetitionRewardVoucher>>.Success(options);
    }

    private static CompetitionRewardVoucher RewardDetails(Voucher v, int? available = null) =>
        new(v.Id, v.Name, v.DiscountType.ToString(), v.DiscountValue, v.MaxDiscountAmount, v.MinimumSpend, v.StartAt, v.EndAt, available);

    private static CompetitionPrizeRequest[] PrizeRequests(Competition competition) =>
        competition.Prizes.Where(p => !p.IsDeleted && p.VoucherId != null)
            .Select(p => new CompetitionPrizeRequest(p.PrizeName, p.Rank, p.Description, p.VoucherId)).ToArray();

    private static void ApplyPrizeChanges(Competition competition, IReadOnlyCollection<CompetitionPrizeRequest> prizes)
    {
        foreach (var existing in competition.Prizes.Where(p => !p.IsDeleted && p.VoucherId != null).ToArray())
            if (!prizes.Any(p => p.Rank == existing.Rank))
                existing.IsDeleted = true;

        foreach (var item in prizes)
        {
            var prize = competition.Prizes.FirstOrDefault(p => !p.IsDeleted && p.Rank == item.Rank);
            if (prize is null)
            {
                prize = new CompetitionPrize { Rank = item.Rank };
                competition.Prizes.Add(prize);
            }
            prize.PrizeName = item.PrizeName.Trim();
            prize.Description = EquipmentService.Clean(item.Description);
            prize.VoucherId = item.VoucherId;
        }
    }

    private async Task<string?> ValidateRewardsAsync(IReadOnlyCollection<CompetitionPrizeRequest> prizes, DateTime at,
        int? excludingCompetitionId, CancellationToken ct)
    {
        if (prizes.Any(p => p.VoucherId is null || p.Rank <= 0 || string.IsNullOrWhiteSpace(p.PrizeName)) ||
            prizes.GroupBy(p => p.Rank).Any(g => g.Count() != 1))
            return "Mỗi phần thưởng cần một thứ hạng riêng và một voucher đã chọn.";

        foreach (var group in prizes.GroupBy(p => p.VoucherId!.Value).OrderBy(g => g.Key))
        {
            var voucher = await _repository.LockRewardVoucherAsync(group.Key, ct);
            var rank = group.Min(p => p.Rank);
            if (voucher is null || voucher.Audience != VoucherAudience.AssignedUsers || voucher.VoucherStatus != VoucherStatus.Active)
                return $"Hạng {rank} cần một voucher cấp riêng đang được phép sử dụng.";
            if (voucher.StartAt > at || voucher.EndAt <= at || voucher.EndAt <= _clock.GetUtcNow().UtcDateTime)
                return $"Voucher {voucher.Name} của hạng {rank} phải còn hiệu lực khi trao giải. Vui lòng chọn voucher khác.";
            if (voucher.MaxRedemptions is { } limit &&
                await _repository.CountCommittedAsync(voucher.Id, excludingCompetitionId, ct) + group.Count() > limit)
                return $"Voucher {voucher.Name} không còn đủ lượt cho {group.Count()} giải đã chọn.";
        }
        return null;
    }

    public Task<Result<CompetitionParticipantResponse>> ConfirmResultAsync(ConfirmCompetitionResultRequest request,
        string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        _repository.WithResultsLockAsync(async () =>
        {
            var join = await _repository.GetParticipantAsync(request.JoinId, cancellationToken);
            if (join == null) return NotFound<CompetitionParticipantResponse>();
            if (join.Status == CompetitionJoinStatus.Completed)
            {
                if ((request.Rank.HasValue && request.Rank != join.Rank) ||
                    (request.Score.HasValue && request.Score != join.Score) ||
                    (EquipmentService.Clean(request.Note) is { } note && note != join.Note))
                    return RewardError<CompetitionParticipantResponse>("Kết quả đã xác nhận và không thể sửa.");
                return Result<CompetitionParticipantResponse>.Success(ToParticipant(join));
            }
            if (join.Status == CompetitionJoinStatus.Disqualified)
                return RewardError<CompetitionParticipantResponse>("Người tham gia đã bị loại.");
            if ((request.Rank ?? join.Rank) == null)
                return RewardError<CompetitionParticipantResponse>("Cần có thứ hạng trước khi duyệt kết quả.");
            join.Rank = request.Rank ?? join.Rank;
            join.Score = request.Score ?? join.Score;
            join.Note = EquipmentService.Clean(request.Note) ?? join.Note;
            var result = await ConfirmRankedAsync(join.CompetitionId, join.Id, managerUserId, ipAddress, cancellationToken);
            return result.IsSuccess ? Result<CompetitionParticipantResponse>.Success(ToParticipant(join))
                : Result<CompetitionParticipantResponse>.Failure(result.ErrorMessage!, result.ErrorCode!, result.StatusCode);
        }, cancellationToken);

    public Task<Result<int>> ConfirmAllResultsAsync(int competitionId, string managerUserId, string? ipAddress, CancellationToken cancellationToken = default) =>
        _repository.WithResultsLockAsync(() => ConfirmRankedAsync(competitionId, null, managerUserId, ipAddress, cancellationToken), cancellationToken);

    private async Task<Result<int>> ConfirmRankedAsync(int id, int? singleJoinId, string manager, string? ip, CancellationToken ct)
    {
        var competition = await _repository.GetCompetitionWithDetailsAsync(id, ct);
        if (competition == null) return NotFound<int>();
        if (competition.Status != CompetitionStatus.Completed)
            return RewardError<int>("Chỉ có thể duyệt kết quả sau khi cuộc thi đã kết thúc.");

        var finalists = await _repository.GetFinalistsAsync(id, ct);
        var pending = finalists.Where(j => j.Rank.HasValue && j.Status != CompetitionJoinStatus.Completed).ToArray();
        if (pending.Length == 0)
            return finalists.Any(j => j.ConfirmedAt.HasValue)
                ? Result<int>.Success(0) : RewardError<int>("Chưa có kết quả có thứ hạng để duyệt.");

        var prizes = competition.Prizes.Where(p => p.VoucherId.HasValue).ToArray();
        var ambiguous = prizes.FirstOrDefault(p => finalists.Count(j => j.Rank == p.Rank) > 1);
        if (ambiguous != null)
            return RewardError<int>($"Hạng {ambiguous.Rank} có nhiều người cùng hạng. Hãy rà soát thứ hạng trước khi trao voucher.");
        if (prizes.GroupBy(p => p.Rank).Any(g => g.Count() > 1))
            return RewardError<int>("Có nhiều phần thưởng cho cùng một hạng. Vui lòng rà soát trước khi duyệt.");

        var awards = pending.Join(prizes, j => j.Rank, p => p.Rank, (j, p) => (Join: j, Prize: p)).ToArray();
        if (singleJoinId.HasValue && awards.Length > 1 && awards.Any(a => a.Join.Id == singleJoinId))
            return RewardError<int>("Cuộc thi có nhiều người đạt giải. Chọn Duyệt tất cả để trao các phần thưởng cùng lúc.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var error = await ValidateFinalRewardsAsync(competition, finalists, now, ct);
        if (error != null) return RewardError<int>(error);

        var selected = pending.Where(j => singleJoinId == null || j.Id == singleJoinId).ToArray();
        foreach (var j in selected)
        {
            j.Status = CompetitionJoinStatus.Completed;
            j.ConfirmedAt = now;
            j.ConfirmedByUserId = manager;
            var prize = awards.FirstOrDefault(a => a.Join.Id == j.Id).Prize;
            if (prize == null) continue;
            var grant = new VoucherGrant { UserId = j.MemberUserId, CompetitionPrizeId = prize.Id, VoucherId = prize.VoucherId!.Value };
            _repository.Add(grant);
            var notification = AuditedWriter.Notify(j.MemberUserId, manager, "Bạn nhận được voucher cuộc thi",
                $"Chúc mừng! Bạn đạt hạng {j.Rank} cuộc thi {competition.CompetitionName} và nhận voucher {prize.Voucher!.Name}. Xem tại Voucher của tôi.",
                JoinEntity, NotificationType.CompetitionResult);
            notification.EntityId = j.Id;
            _repository.Add(notification);
        }
        var audit = AuditedWriter.Audit(manager, AuditActionType.Update, Entity, ip, newValue: $"Confirmed={selected.Length}");
        await AuditedWriter.SaveChangedAsync(_repository, competition, audit, null, ct);
        return Result<int>.Success(selected.Length);
    }

    private async Task<string?> ValidateFinalRewardsAsync(Competition competition, IReadOnlyCollection<CompetitionJoin> finalists,
        DateTime now, CancellationToken ct)
    {
        // Confirmed legacy results stay historical. Only pending ranks can receive grants.
        var issuedRanks = finalists.Where(j => j.Status == CompetitionJoinStatus.Completed).Select(j => j.Rank).ToHashSet();
        var remaining = PrizeRequests(competition).Where(p => !issuedRanks.Contains(p.Rank)).ToArray();
        var error = await ValidateRewardsAsync(remaining, now, competition.Id, ct);
        if (error != null) return error;
        foreach (var prize in remaining)
        {
            var join = finalists.FirstOrDefault(j => j.Rank == prize.Rank);
            if (join == null) continue;
            var voucher = await _repository.LockRewardVoucherAsync(prize.VoucherId!.Value, ct);
            if (voucher!.MaxRedemptionsPerUser is { } limit &&
                await _repository.CountUserCommitmentsAsync(voucher.Id, join.MemberUserId, ct) >= limit)
                return $"Người đạt hạng {prize.Rank} đã hết lượt nhận voucher {voucher.Name}. Vui lòng chọn voucher khác cho hạng này.";
        }
        return null;
    }

    public Task<Result<CompetitionResponse>> ReplaceUnusableRewardsAsync(int id, IReadOnlyCollection<CompetitionPrizeRequest> prizes,
        string managerUserId, string? ipAddress, CancellationToken ct) =>
        _repository.WithResultsLockAsync(async () =>
        {
            var competition = await _repository.GetCompetitionWithDetailsAsync(id, ct);
            if (competition == null) return NotFound<CompetitionResponse>();
            if (competition.Status != CompetitionStatus.Completed)
                return RewardError<CompetitionResponse>("Chỉ thay voucher chưa thể trao sau khi cuộc thi kết thúc.");
            var finalists = await _repository.GetFinalistsAsync(id, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            var before = PrizeRequests(competition);
            if (await ValidateFinalRewardsAsync(competition, finalists, now, ct) == null)
                return RewardError<CompetitionResponse>("Các phần thưởng hiện tại đủ điều kiện trao.");
            if (!before.Select(p => p.Rank).Order().SequenceEqual(prizes.Select(p => p.Rank).Order()))
                return RewardError<CompetitionResponse>("Cần giữ nguyên các thứ hạng đã công bố.");
            foreach (var prize in prizes)
            {
                var previous = before.Single(p => p.Rank == prize.Rank);
                if (previous.VoucherId == prize.VoucherId) continue;
                if (finalists.Any(j => j.Rank == prize.Rank && j.ConfirmedAt.HasValue))
                    return RewardError<CompetitionResponse>("Phần thưởng đã xác nhận và không thể thay đổi.");
                var one = new Competition { Id = id, Prizes = [competition.Prizes.Single(p => p.Rank == prize.Rank)] };
                if (await ValidateFinalRewardsAsync(one, finalists, now, ct) == null)
                    return RewardError<CompetitionResponse>($"Voucher của hạng {prize.Rank} vẫn đủ điều kiện trao.");
            }
            // Only voucher IDs for unusable, unissued ranks may change here.
            foreach (var prize in prizes)
                competition.Prizes.Single(p => p.Rank == prize.Rank).VoucherId = prize.VoucherId;
            if (prizes.Any(p => p.VoucherId is null))
                return RewardError<CompetitionResponse>("Cần chọn voucher cho từng hạng.");
            var error = await ValidateFinalRewardsAsync(competition, finalists, now, ct);
            if (error != null) return RewardError<CompetitionResponse>(error);
            var audit = AuditedWriter.Audit(managerUserId, AuditActionType.Update, Entity, ipAddress,
                string.Join(", ", before.Select(p => $"{p.Rank}={p.VoucherId}")), string.Join(", ", prizes.Select(p => $"{p.Rank}={p.VoucherId}")));
            await AuditedWriter.SaveChangedAsync(_repository, competition, audit, null, ct);
            return await GetCompetitionAsync(id, ct, true);
        }, ct);

    private static Result<T> RewardError<T>(string message) =>
        Result<T>.Failure(message, "COMPETITION_REWARD_INVALID", 409);
}
