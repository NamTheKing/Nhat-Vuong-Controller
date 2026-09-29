namespace NhatVuong.Application.Abstractions;

/// <summary>
/// Persistence port. The application core composes LINQ queries; the persistence adapter executes them.
/// Scoped: one instance per request or scheduler tick, never shared across threads.
/// </summary>
public interface IDataStore
{
    IQueryable<T> Query<T>() where T : class;

    void Add<T>(T entity) where T : class;

    void Remove<T>(T entity) where T : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct = default);
}

/// <summary>
/// Async materialisation without a reference to an ORM: EF Core queryables implement <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
public static class QueryableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> query, CancellationToken ct = default)
    {
        if (query is not IAsyncEnumerable<T> asyncEnumerable)
        {
            return query.ToList();
        }

        var list = new List<T>();
        await foreach (var item in asyncEnumerable.WithCancellation(ct))
        {
            list.Add(item);
        }

        return list;
    }

    public static async Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, CancellationToken ct = default) =>
        (await query.Take(1).ToListAsync(ct)).FirstOrDefault();

    public static async Task<bool> AnyAsync<T>(this IQueryable<T> query, CancellationToken ct = default) =>
        (await query.Select(_ => 1).Take(1).ToListAsync(ct)).Count > 0;

    public static Task<bool> AnyAsync<T>(
        this IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        query.Where(predicate).AnyAsync(ct);
}
