using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class PromotionRepository(VfmsDbContext context) : AuditedRepositoryBase(context), IPromotionRepository
{
    public async Task<IReadOnlyList<PromotionReadModel>> ListAsync(CancellationToken ct) =>
        await Project(Context.Vouchers.AsNoTracking().Where(v => !v.IsDeleted)
            .OrderBy(v => v.Name).ThenBy(v => v.Id)).ToListAsync(ct);

    public Task<PromotionReadModel?> GetAsync(int id, CancellationToken ct) =>
        Project(Context.Vouchers.AsNoTracking().Where(v => v.Id == id && !v.IsDeleted)).SingleOrDefaultAsync(ct);

    public Task<Voucher?> GetForUpdateAsync(int id, CancellationToken ct)
    {
        var vouchers = Context.Database.IsSqlServer()
            ? Context.Vouchers.FromSqlInterpolated($"SELECT * FROM [Vouchers] WITH (XLOCK, HOLDLOCK) WHERE [Id] = {id}")
            : Context.Vouchers;
        return vouchers.SingleOrDefaultAsync(v => v.Id == id && !v.IsDeleted, ct);
    }

    public Task<int> GetCommittedCapacityAsync(int id, CancellationToken ct) =>
        CompetitionRewardCapacity.CommittedAsync(Context, id, null, ct);

    public async Task<int> GetMaxCommittedPerUserAsync(int id, CancellationToken ct) =>
        await Context.VoucherRedemptions.IgnoreQueryFilters()
            .Where(r => r.VoucherId == id && (r.RedemptionStatus == VoucherRedemptionStatus.Confirmed ||
                r.RedemptionStatus == VoucherRedemptionStatus.Reserved))
            .GroupBy(r => r.Order.PurchaserUserId).Select(group => (int?)group.Count()).MaxAsync(ct) ?? 0;

    public async Task<bool> HasRewardCommitmentsAsync(int id, CancellationToken ct) =>
        await Context.VoucherGrants.IgnoreQueryFilters().AnyAsync(g => g.VoucherId == id, ct) ||
        await Context.CompetitionPrizes.AnyAsync(p => p.VoucherId == id &&
            (p.Competition.Status == CompetitionStatus.Published || p.Competition.Status == CompetitionStatus.Ongoing ||
             p.Competition.Status == CompetitionStatus.Completed), ct);

    public Task<bool> CodeExistsAsync(string normalizedCode, int? excludingId, CancellationToken ct)
    {
        // Include hidden vouchers: their unique codes and transaction history remain reserved.
        // The SQL Server update/range lock also serializes competing creates for the same code.
        var vouchers = Context.Database.IsSqlServer()
            ? Context.Vouchers.FromSqlInterpolated($"SELECT * FROM [Vouchers] WITH (UPDLOCK, HOLDLOCK) WHERE UPPER(LTRIM(RTRIM([Code]))) = {normalizedCode}")
            : Context.Vouchers.Where(v => v.Code.Trim().ToUpper() == normalizedCode);
        return vouchers.IgnoreQueryFilters().AnyAsync(v => excludingId == null || v.Id != excludingId, ct);
    }

    public async Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await work();
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_Vouchers_Code", StringComparison.OrdinalIgnoreCase))
        {
            throw new PromotionCodeConflictException(exception);
        }
    }

    private IQueryable<PromotionReadModel> Project(IQueryable<Voucher> vouchers) => vouchers.Select(v =>
        new PromotionReadModel(v.Id, v.Name, v.Code, v.DiscountType, v.DiscountValue,
            v.MinimumSpend, v.MaxDiscountAmount, v.MaxRedemptions,
            Context.VoucherRedemptions.IgnoreQueryFilters().Count(r =>
                r.VoucherId == v.Id && r.RedemptionStatus == VoucherRedemptionStatus.Confirmed),
            v.StartAt, v.EndAt, v.VoucherStatus, v.Audience, v.MaxRedemptionsPerUser, v.NewCustomerDuration,
            v.NewCustomerUnit, Context.VoucherGrants.Count(grant => grant.VoucherId == v.Id)));
}
