using NhatVuong.Application.Abstractions;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Policy;

/// <summary>Process-wide cache of the policy row; invalidated when an administrator saves new settings.</summary>
public sealed class PolicyCache
{
    private PolicySettings? _current;

    public PolicySettings? Current => Volatile.Read(ref _current)?.Clone();

    public void Set(PolicySettings settings) => Volatile.Write(ref _current, settings.Clone());
}

public sealed class PolicyProvider(IDataStore store, PolicyCache cache)
{
    public async Task<PolicySettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.Current is { } cached)
        {
            return cached;
        }

        var stored = await store.Query<PolicySettings>()
            .Where(p => p.Id == PolicySettings.SingletonId)
            .FirstOrDefaultAsync(ct);

        if (stored is null)
        {
            stored = new PolicySettings();
            store.Add(stored);
            await store.SaveChangesAsync(ct);
        }

        cache.Set(stored);
        return stored.Clone();
    }
}
