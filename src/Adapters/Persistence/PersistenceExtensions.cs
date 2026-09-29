using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NhatVuong.Application.Abstractions;

namespace NhatVuong.Adapters.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary><c>Sqlite</c> (development, tests) or <c>Postgres</c> (staging, production).</summary>
    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=nhatvuong.db";
}

public static class PersistenceExtensions
{
    public static IServiceCollection AddNvcPersistence(this IServiceCollection services, DatabaseOptions options)
    {
        services.AddDbContext<NvcDbContext>(builder => Configure(builder, options));
        services.AddScoped<IDataStore, EfDataStore>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }

    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions options)
    {
        // Single-row lookups (Take(1) by key) are intentional; the warning is noise here.
        builder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.RowLimitingOperationWithoutOrderByWarning));

        if (string.Equals(options.Provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseNpgsql(options.ConnectionString, npgsql => npgsql.EnableRetryOnFailure());
        }
        else
        {
            builder.UseSqlite(options.ConnectionString);
        }
    }
}
