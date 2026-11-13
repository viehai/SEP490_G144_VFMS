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
            Apply(voucher, request);
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

            var usedCount = await repository.GetUsedCountAsync(id, ct);
            if (request.MaxRedemptions is { } limit && limit < usedCount)
                return Result<PromotionResponse>.Failure(
                    "Tổng lượt sử dụng không được thấp hơn số lượt đã sử dụng.", "PROMOTION_USAGE_LIMIT_TOO_LOW");
            if (await repository.CodeExistsAsync(NormalizeCode(request.Code!), id, ct))
                return DuplicateCode();

            var before = Configuration(voucher);
            Apply(voucher, request);
            var after = Configuration(voucher);
            if (before != after)
            {
                var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, nameof(Voucher),
                    ipAddress, JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
                await AuditedWriter.SaveChangedAsync(repository, voucher, audit, notifications: null, ct);
            }
            return Success(Read(voucher, usedCount));
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

            var usedCount = await repository.GetUsedCountAsync(id, ct);
            if (IsEnabled(voucher.VoucherStatus) != request.IsEnabled!.Value)
            {
                var before = Snapshot(voucher);
                voucher.VoucherStatus = request.IsEnabled.Value ? VoucherStatus.Active : VoucherStatus.Inactive;
                var audit = AuditedWriter.Audit(actorUserId, AuditActionType.Update, nameof(Voucher),
                    ipAddress, before, Snapshot(voucher));
                await AuditedWriter.SaveChangedAsync(repository, voucher, audit, notifications: null, ct);
            }
            return Success(Read(voucher, usedCount));
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
        var status = !enabled ? PromotionStatus.Disabled
            : now >= promotion.EndAt || promotion.MaxRedemptions is { } limit && promotion.UsedCount >= limit
                ? PromotionStatus.Expired
                : now < promotion.StartAt ? PromotionStatus.Scheduled : PromotionStatus.Active;
        return new(promotion.Id, promotion.Name, promotion.Code,
            promotion.DiscountType == DiscountType.PERCENT ? PromotionDiscountType.Percentage : PromotionDiscountType.Fixed,
            promotion.DiscountValue, promotion.MinimumSpend, promotion.MaxDiscountAmount, promotion.MaxRedemptions,
            promotion.UsedCount, DateTime.SpecifyKind(promotion.StartAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(promotion.EndAt, DateTimeKind.Utc), enabled, status);
    }

    private static void Apply(Voucher voucher, PromotionRequest request)
    {
        voucher.Name = request.Name!.Trim();
        voucher.Code = NormalizeCode(request.Code!);
        voucher.DiscountType = request.DiscountType == PromotionDiscountType.Percentage ? DiscountType.PERCENT : DiscountType.FIXED;
        voucher.DiscountValue = request.DiscountValue!.Value;
        voucher.MinimumSpend = request.MinimumSpend;
        voucher.MaxDiscountAmount = request.DiscountType == PromotionDiscountType.Percentage ? request.MaxDiscountAmount : null;
        voucher.MaxRedemptions = request.MaxRedemptions;
        voucher.StartAt = request.StartAt!.Value;
        voucher.EndAt = request.EndAt!.Value;
        // Active is the persisted enabled flag. Dates and quota always control the effective status.
        if (!request.IsEnabled!.Value)
            voucher.VoucherStatus = VoucherStatus.Inactive;
        else if (!IsEnabled(voucher.VoucherStatus))
            voucher.VoucherStatus = VoucherStatus.Active;
    }

    private static PromotionReadModel Read(Voucher voucher, int usedCount) =>
        new(voucher.Id, voucher.Name, voucher.Code, voucher.DiscountType, voucher.DiscountValue,
            voucher.MinimumSpend, voucher.MaxDiscountAmount, voucher.MaxRedemptions, usedCount,
            voucher.StartAt, voucher.EndAt, voucher.VoucherStatus);

    private static string Snapshot(Voucher voucher) => JsonSerializer.Serialize(Configuration(voucher));

    // Compare numeric values structurally: SQL decimal(18,2) preserves trailing-zero scale,
    // which must not make a semantically identical JSON request look like a configuration change.
    private static PromotionConfiguration Configuration(Voucher voucher) => new(
        voucher.Name, voucher.Code, voucher.DiscountType, voucher.DiscountValue, voucher.MinimumSpend,
        voucher.MaxDiscountAmount, voucher.MaxRedemptions,
        DateTime.SpecifyKind(voucher.StartAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(voucher.EndAt, DateTimeKind.Utc), IsEnabled(voucher.VoucherStatus));

    private sealed record PromotionConfiguration(
        string Name, string Code, DiscountType DiscountType, decimal DiscountValue, decimal? MinimumSpend,
        decimal? MaxDiscountAmount, int? MaxRedemptions, DateTime StartAt, DateTime EndAt, bool IsEnabled);

    private static bool IsEnabled(VoucherStatus status) => status != VoucherStatus.Inactive;
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static Result<PromotionResponse> NotFound() =>
        Result<PromotionResponse>.Failure("Không tìm thấy chương trình khuyến mãi.", "PROMOTION_NOT_FOUND", 404);
    private static Result<PromotionResponse> DuplicateCode() =>
        Result<PromotionResponse>.Failure("Mã voucher đã tồn tại.", "PROMOTION_CODE_EXISTS", 409);
    private static Result<PromotionResponse> Invalid(FluentValidation.Results.ValidationResult validation) =>
        Result<PromotionResponse>.Failure(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()),
            "PROMOTION_VALIDATION_FAILED");
}
