using Microsoft.EntityFrameworkCore;
using NhatVuong.Application.Abstractions;

namespace NhatVuong.Adapters.Persistence;

/// <summary>EF Core implementation of the persistence port.</summary>
public sealed class EfDataStore(NvcDbContext db) : IDataStore
{
    public IQueryable<T> Query<T>() where T : class => db.Set<T>();

    public void Add<T>(T entity) where T : class => db.Set<T>().Add(entity);

    public void Remove<T>(T entity) where T : class => db.Set<T>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct = default) =>
        EntityFrameworkQueryableExtensions.CountAsync(query, ct);
}
