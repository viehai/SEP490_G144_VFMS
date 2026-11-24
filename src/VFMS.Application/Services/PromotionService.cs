using System.Text.Json;
using FluentValidation;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class PromotionService(
    IPromotionRepository repository, IValidator<CreatePromotionRequest> createValidator,
    IValidator<UpdatePromotionRequest> updateValidator, IValidator<SetPromotionEnabledRequest> enabledValidator,
    TimeProvider timeProvider) : IPromotionService
{
    public async Task<Result<IReadOnlyList<PromotionResponse>>> ListAsync(CancellationToken ct = default)
    {
        var promotions = await repository.ListAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return Result<IReadOnlyList<PromotionResponse>>.Success(promotions.Select(p => ToResponse(p, now)).ToArray());
    }

    public async Task<Result<PromotionResponse>> GetAsync(int id, CancellationToken ct = default)
    {
        var promotion = await repository.GetAsync(id, ct);
        return promotion is null ? NotFound() : Success(promotion);
    }

    public async Task<Result<PromotionResponse>> CreateAsync(
        CreatePromotionRequest request, string actorUserId, string? ipAddress, CancellationToken ct = default)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Invalid(validation);

        return await WriteAsync(async () =>
        {
            var code = NormalizeCode(request.Code!);
            if (await repository.CodeExistsAsync(code, null, ct))
                return DuplicateCode();

            var voucher = new Voucher { CreatedByUserId = actorUserId };
            Apply(voucher, RequestedConfiguration(voucher, request));
            var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Create, nameof(Voucher), ipAddress,
                newValue: Snapshot(voucher));
            await AuditedWriter.SaveNewAsync(repository, voucher, audit, notifications: null, ct);
            return Success(Read(voucher, 0), 201);
        }, ct);
    }

    public async Task<Result<PromotionResponse>> UpdateAsync(
        int id, UpdatePromotionRequest request, string actorUserId, string? ipAddress, CancellationToken ct = default)
    {
        var validation = await updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Invalid(validation);

        return await WriteAsync(async () =>
        {
            var voucher = await repository.GetForUpdateAsync(id, ct);
            if (voucher is null)
                return NotFound();

            var before = Configuration(voucher);
            var after = RequestedConfiguration(voucher, request);
            if (after != (before with { MaxRedemptions = after.MaxRedemptions }) &&
                await repository.HasRewardCommitmentsAsync(id, ct))
                return CommittedReward();
            if (after.MaxRedemptions is { } limit && limit < await repository.GetCommittedCapacityAsync(id, ct))
                return Result<PromotionResponse>.Failure(
                    "Tổng lượt sử dụng không đủ cho các lượt đã dùng, đang giữ hoặc đã cam kết làm phần thưởng.", "PROMOTION_USAGE_LIMIT_TOO_LOW");
            if (after.MaxRedemptionsPerUser is { } perUser && perUser < await repository.GetMaxCommittedPerUserAsync(id, ct))
                return Result<PromotionResponse>.Failure(
                    "Hạn mức mỗi tài khoản thấp hơn số lượt một khách hàng đã dùng hoặc đang giữ.", "PROMOTION_ACCOUNT_LIMIT_TOO_LOW");
            if (await repository.CodeExistsAsync(after.Code, id, ct))
                return DuplicateCode();

            if (before != after)
            {
                Apply(voucher, after);
                var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, nameof(Voucher),
                    ipAddress, JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
                await AuditedWriter.SaveChangedAsync(repository, voucher, audit, notifications: null, ct);
            }
            return Success((await repository.GetAsync(id, ct))!);
        }, ct);
    }

    public async Task<Result<PromotionResponse>> SetEnabledAsync(
        int id, SetPromotionEnabledRequest request, string actorUserId, string? ipAddress, CancellationToken ct = default)
    {
        var validation = await enabledValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Invalid(validation);

        return await WriteAsync(async () =>
        {
            var voucher = await repository.GetForUpdateAsync(id, ct);
            if (voucher is null)
                return NotFound();

            if (IsEnabled(voucher.VoucherStatus) != request.IsEnabled!.Value)
            {
                if (!request.IsEnabled.Value && await repository.HasRewardCommitmentsAsync(id, ct))
                    return CommittedReward();
                var before = Snapshot(voucher);
                voucher.VoucherStatus = request.IsEnabled.Value ? VoucherStatus.Active : VoucherStatus.Inactive;
                var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, nameof(Voucher),
                    ipAddress, before, Snapshot(voucher));
                await AuditedWriter.SaveChangedAsync(repository, voucher, audit, notifications: null, ct);
            }
            return Success((await repository.GetAsync(id, ct))!);
        }, ct);
    }

    private async Task<Result<PromotionResponse>> WriteAsync(Func<Task<Result<PromotionResponse>>> work, CancellationToken ct)
    {
        var result = NotFound();
        try
        {
            await repository.ExecuteInSerializableTransactionAsync(async () => result = await work(), ct);
            return result;
        }
        catch (PromotionCodeConflictException)
        {
            return DuplicateCode();
        }
    }

    private Result<PromotionResponse> Success(PromotionReadModel promotion, int statusCode = 200) =>
        Result<PromotionResponse>.Success(ToResponse(promotion, timeProvider.GetUtcNow().UtcDateTime), statusCode);

    private static PromotionResponse ToResponse(PromotionReadModel promotion, DateTime now)
    {
        var enabled = IsEnabled(promotion.VoucherStatus);
        var status = VoucherRules.EffectiveStatus(promotion.VoucherStatus, promotion.StartAt, promotion.EndAt,
            promotion.MaxRedemptions, promotion.UsedCount, now);
        return new(promotion.Id, promotion.Name, promotion.Code,
            promotion.DiscountType == DiscountType.PERCENT ? PromotionDiscountType.Percentage : PromotionDiscountType.Fixed,
            promotion.DiscountValue, promotion.MinimumSpend, promotion.MaxDiscountAmount, promotion.MaxRedemptions,
            promotion.UsedCount, DateTime.SpecifyKind(promotion.StartAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(promotion.EndAt, DateTimeKind.Utc), enabled, status,
            (PromotionAudience)promotion.Audience, promotion.MaxRedemptionsPerUser, promotion.NewCustomerDuration,
            (PromotionDurationUnit?)promotion.NewCustomerUnit, promotion.GrantedCount);
    }

    private static PromotionConfiguration RequestedConfiguration(Voucher voucher, PromotionRequest request) => new(
        request.Name!.Trim(), NormalizeCode(request.Code!),
        request.DiscountType == PromotionDiscountType.Percentage ? DiscountType.PERCENT : DiscountType.FIXED,
        request.DiscountValue!.Value, request.MinimumSpend,
        request.DiscountType == PromotionDiscountType.Percentage ? request.MaxDiscountAmount : null,
        request.MaxRedemptions, request.StartAt!.Value, request.EndAt!.Value, request.IsEnabled!.Value,
        (VoucherAudience?)request.Audience ?? voucher.Audience,
        request.Audience.HasValue ? request.MaxRedemptionsPerUser : request.MaxRedemptionsPerUser ?? voucher.MaxRedemptionsPerUser,
        request.Audience.HasValue ? request.NewCustomerDuration : voucher.NewCustomerDuration,
        request.Audience.HasValue ? (FreezeTierUnit?)request.NewCustomerUnit : voucher.NewCustomerUnit);

    private static void Apply(Voucher voucher, PromotionConfiguration config)
    {
        voucher.Name = config.Name;
        voucher.Code = config.Code;
        voucher.DiscountType = config.DiscountType;
        voucher.DiscountValue = config.DiscountValue;
        voucher.MinimumSpend = config.MinimumSpend;
        voucher.MaxDiscountAmount = config.MaxDiscountAmount;
        voucher.MaxRedemptions = config.MaxRedemptions;
        voucher.MaxRedemptionsPerUser = config.MaxRedemptionsPerUser;
        voucher.Audience = config.Audience;
        voucher.NewCustomerDuration = config.NewCustomerDuration;
        voucher.NewCustomerUnit = config.NewCustomerUnit;
        voucher.StartAt = config.StartAt;
        voucher.EndAt = config.EndAt;
        // Active is the persisted enabled flag. Dates and quota always control the effective status.
        if (!config.IsEnabled)
            voucher.VoucherStatus = VoucherStatus.Inactive;
        else if (!IsEnabled(voucher.VoucherStatus))
            voucher.VoucherStatus = VoucherStatus.Active;
    }

    private static PromotionReadModel Read(Voucher voucher, int usedCount) =>
        new(voucher.Id, voucher.Name, voucher.Code, voucher.DiscountType, voucher.DiscountValue,
            voucher.MinimumSpend, voucher.MaxDiscountAmount, voucher.MaxRedemptions, usedCount,
            voucher.StartAt, voucher.EndAt, voucher.VoucherStatus, voucher.Audience, voucher.MaxRedemptionsPerUser,
            voucher.NewCustomerDuration, voucher.NewCustomerUnit, 0);

    private static string Snapshot(Voucher voucher) => JsonSerializer.Serialize(Configuration(voucher));

    // Compare numeric values structurally: SQL decimal(18,2) preserves trailing-zero scale,
    // which must not make a semantically identical JSON request look like a configuration change.
    private static PromotionConfiguration Configuration(Voucher voucher) => new(
        voucher.Name, voucher.Code, voucher.DiscountType, voucher.DiscountValue, voucher.MinimumSpend,
        voucher.MaxDiscountAmount, voucher.MaxRedemptions,
        DateTime.SpecifyKind(voucher.StartAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(voucher.EndAt, DateTimeKind.Utc), IsEnabled(voucher.VoucherStatus),
        voucher.Audience, voucher.MaxRedemptionsPerUser, voucher.NewCustomerDuration, voucher.NewCustomerUnit);

    private sealed record PromotionConfiguration(
        string Name, string Code, DiscountType DiscountType, decimal DiscountValue, decimal? MinimumSpend,
        decimal? MaxDiscountAmount, int? MaxRedemptions, DateTime StartAt, DateTime EndAt, bool IsEnabled,
        VoucherAudience Audience, int? MaxRedemptionsPerUser, int? NewCustomerDuration, FreezeTierUnit? NewCustomerUnit);

    private static Result<PromotionResponse> CommittedReward() =>
        Result<PromotionResponse>.Failure("Voucher đã được cam kết làm giải thưởng hoặc cấp cho hội viên. Chỉ có thể chỉnh tổng lượt sử dụng trong phạm vi còn đủ cho các cam kết.", "PROMOTION_REWARD_COMMITTED", 409);
    private static bool IsEnabled(VoucherStatus status) => VoucherRules.IsEnabled(status);
    private static string NormalizeCode(string code) => VoucherRules.NormalizeCode(code);
    private static Result<PromotionResponse> NotFound() =>
        Result<PromotionResponse>.Failure("Không tìm thấy chương trình khuyến mãi.", "PROMOTION_NOT_FOUND", 404);
    private static Result<PromotionResponse> DuplicateCode() =>
        Result<PromotionResponse>.Failure("Mã voucher đã tồn tại.", "PROMOTION_CODE_EXISTS", 409);
    private static Result<PromotionResponse> Invalid(FluentValidation.Results.ValidationResult validation) =>
        Result<PromotionResponse>.Failure(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()),
            "PROMOTION_VALIDATION_FAILED");
}
