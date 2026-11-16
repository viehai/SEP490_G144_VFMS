using VFMS.Contracts.Common;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Interfaces;

public interface IPromotionService
{
    Task<Result<IReadOnlyList<PromotionResponse>>> ListAsync(CancellationToken ct = default);
    Task<Result<PromotionResponse>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<PromotionResponse>> CreateAsync(CreatePromotionRequest request, string actorUserId,
        string? ipAddress, CancellationToken ct = default);
    Task<Result<PromotionResponse>> UpdateAsync(int id, UpdatePromotionRequest request, string actorUserId,
        string? ipAddress, CancellationToken ct = default);
    Task<Result<PromotionResponse>> SetEnabledAsync(int id, SetPromotionEnabledRequest request, string actorUserId,
        string? ipAddress, CancellationToken ct = default);
}

public interface IPromotionRepository : IAuditedRepository
{
    Task<IReadOnlyList<PromotionReadModel>> ListAsync(CancellationToken ct);
    Task<PromotionReadModel?> GetAsync(int id, CancellationToken ct);
    Task<Voucher?> GetForUpdateAsync(int id, CancellationToken ct);
    Task<int> GetUsedCountAsync(int id, CancellationToken ct);
    Task<int> GetCommittedCapacityAsync(int id, CancellationToken ct);
    Task<bool> CodeExistsAsync(string normalizedCode, int? excludingId, CancellationToken ct);
    Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct);
}

public sealed record PromotionReadModel(
    int Id, string Name, string Code, DiscountType DiscountType, decimal DiscountValue,
    decimal? MinimumSpend, decimal? MaxDiscountAmount, int? MaxRedemptions, int UsedCount,
    DateTime StartAt, DateTime EndAt, VoucherStatus VoucherStatus);

/// <summary>Persistence translates a competing insert's unique-code failure without leaking database types.</summary>
public sealed class PromotionCodeConflictException(Exception innerException)
    : Exception("The voucher code already exists.", innerException);
