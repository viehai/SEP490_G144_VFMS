using System.Linq.Expressions;
using VFMS.Application.Interfaces;

namespace VFMS.Application.Services;

// Keeps PT query chains readable while execution stays in the Infrastructure store (no EF dependency here).
internal static class PersonalTrainingQueryExtensions
{
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store, CancellationToken ct) =>
        store.List(query, ct);

    public static async Task<T[]> ToArrayAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store, CancellationToken ct) =>
        [.. await store.List(query, ct)];

    public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store, CancellationToken ct) =>
        store.Single(query, ct);

    public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store,
        Expression<Func<T, bool>> predicate, CancellationToken ct) => store.Single(query.Where(predicate), ct);

    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store, CancellationToken ct) =>
        store.First(query, ct);

    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store,
        Expression<Func<T, bool>> predicate, CancellationToken ct) => store.First(query.Where(predicate), ct);

    public static Task<bool> AnyAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store, CancellationToken ct) =>
        store.Any(query, ct);

    public static Task<bool> AnyAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store,
        Expression<Func<T, bool>> predicate, CancellationToken ct) => store.Any(query.Where(predicate), ct);

    public static Task<int> CountAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store, CancellationToken ct) =>
        store.Count(query, ct);

    public static Task<int> CountAsync<T>(this IQueryable<T> query, IPersonalTrainingStore store,
        Expression<Func<T, bool>> predicate, CancellationToken ct) => store.Count(query.Where(predicate), ct);

    public static Task<TResult?> MaxAsync<T, TResult>(this IQueryable<T> query, IPersonalTrainingStore store,
        Expression<Func<T, TResult>> selector, CancellationToken ct) => store.Max(query.Select(selector), ct);

    /// <summary>Navigation paths such as "Order.PaymentTransaction.Invoice"; several paths use split queries.</summary>
    public static IQueryable<T> Including<T>(this IQueryable<T> query, IPersonalTrainingStore store, params string[] paths)
        where T : class => store.Include(query, paths);

    public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> query, IPersonalTrainingStore store) where T : class =>
        store.NoTracking(query);
}
