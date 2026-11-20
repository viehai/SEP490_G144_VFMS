using VFMS.Contracts.Common;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

// PT queries are composed in the Application layer without an EF dependency; execution belongs to Infrastructure.
public interface IPersonalTrainingStore
{
    IQueryable<T> Query<T>() where T : class;
    /// <summary>Loads navigation paths such as "Order.PaymentTransaction.Invoice"; several paths use split queries.</summary>
    IQueryable<T> Include<T>(IQueryable<T> query, params string[] paths) where T : class;
    IQueryable<T> NoTracking<T>(IQueryable<T> query) where T : class;

    Task<List<T>> List<T>(IQueryable<T> query, CancellationToken ct);
    Task<T?> First<T>(IQueryable<T> query, CancellationToken ct);
    Task<T?> Single<T>(IQueryable<T> query, CancellationToken ct);
    Task<bool> Any<T>(IQueryable<T> query, CancellationToken ct);
    Task<int> Count<T>(IQueryable<T> query, CancellationToken ct);
    Task<T?> Max<T>(IQueryable<T> query, CancellationToken ct);

    void Add<T>(T entity) where T : class;
    void AddRange<T>(IEnumerable<T> entities) where T : class;
    void Remove<T>(T entity) where T : class;
    void RemoveRange<T>(IEnumerable<T> entities) where T : class;
    Task Save(CancellationToken ct);
    /// <summary>Runs the work in a serializable transaction; deadlocks and unique-key races become a 409 result.</summary>
    Task<Result<T>> Write<T>(Func<Task<Result<T>>> work, CancellationToken ct);

    /// <summary>True when a package is referenced by orders or memberships, including soft-deleted history.</summary>
    Task<bool> PackageHasHistory(int packageId, CancellationToken ct);

    Task<bool> IsTransferRecorded(PaymentTransaction payment, string reference, CancellationToken ct);
    void RecordExtraTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now);
    void RecordUnappliedTransfer(PaymentTransaction payment, SepayTransfer transfer, DateTime now);
}
