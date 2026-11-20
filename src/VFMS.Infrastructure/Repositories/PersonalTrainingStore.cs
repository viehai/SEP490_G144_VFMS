using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Payments;

namespace VFMS.Infrastructure.Repositories;

public sealed class PersonalTrainingStore(VfmsDbContext db) : IPersonalTrainingStore
{
    public IQueryable<T> Query<T>() where T : class => db.Set<T>();

    public IQueryable<T> Include<T>(IQueryable<T> query, params string[] paths) where T : class
    {
        foreach (var path in paths)
            query = query.Include(path);
        return paths.Length > 1 ? query.AsSplitQuery() : query;
    }

    public IQueryable<T> NoTracking<T>(IQueryable<T> query) where T : class => query.AsNoTracking();

    public Task<List<T>> List<T>(IQueryable<T> query, CancellationToken ct) => query.ToListAsync(ct);
    public Task<T?> First<T>(IQueryable<T> query, CancellationToken ct) => query.FirstOrDefaultAsync(ct);
    public Task<T?> Single<T>(IQueryable<T> query, CancellationToken ct) => query.SingleOrDefaultAsync(ct);
    public Task<bool> Any<T>(IQueryable<T> query, CancellationToken ct) => query.AnyAsync(ct);
    public Task<int> Count<T>(IQueryable<T> query, CancellationToken ct) => query.CountAsync(ct);
    public Task<T?> Max<T>(IQueryable<T> query, CancellationToken ct) => query.MaxAsync(ct)!;

    public void Add<T>(T entity) where T : class => db.Set<T>().Add(entity);
    public void AddRange<T>(IEnumerable<T> entities) where T : class => db.Set<T>().AddRange(entities);
    public void Remove<T>(T entity) where T : class => db.Set<T>().Remove(entity);
    public void RemoveRange<T>(IEnumerable<T> entities) where T : class => db.Set<T>().RemoveRange(entities);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<Result<T>> Write<T>(Func<Task<Result<T>>> work, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            return Result<T>.Failure("Lịch vừa thay đổi. Vui lòng tải lại và thử lại.", "PT_CONFLICT", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
        {
            return Result<T>.Failure("Dữ liệu vừa thay đổi. Vui lòng tải lại và thử lại.", "PT_CONFLICT", 409);
        }
    }

    public Task<bool> PackageHasHistory(int packageId, CancellationToken ct) =>
        db.Packages.IgnoreQueryFilters().AnyAsync(p => p.Id == packageId &&
            (p.OrderItems.Any() || p.Components.Any(c => c.MemberMemberships.Any())), ct);

    public Task<bool> IsTransferRecorded(PaymentTransaction payment, string reference, CancellationToken ct) =>
        CheckoutRefunds.IsRecorded(db, payment, reference, ct);

    public void RecordExtraTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now) =>
        CheckoutRefunds.RecordExtraTransfer(db, payment, transfer, now);

    public void RecordUnappliedTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now) =>
        CheckoutRefunds.RecordUnappliedTransfer(db, payment, transfer, now);
}
