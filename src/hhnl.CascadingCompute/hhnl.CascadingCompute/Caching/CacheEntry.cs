using System.Collections.Concurrent;

namespace hhnl.CascadingCompute.Caching;

public class CacheEntry<TParameters, TResult> : IDependentCacheEntry
    where TParameters : notnull
{
    private static long idCounter = 0;
    private long _id = idCounter++;

    private List<WeakReference<IDependentCacheEntry>> _dependents = [];
    private readonly TParameters _parameters;
    private readonly ConcurrentDictionary<TParameters, CacheEntry<TParameters, TResult>> _cache;

    public TResult Value { get; set; }

    public DateTime? Expiration { get; }

    public CacheEntry(TParameters parameters, ConcurrentDictionary<TParameters, CacheEntry<TParameters, TResult>> cache, TimeSpan? ttl = null)
    {
        _cache = cache;
        _parameters = parameters;
        Expiration = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null;
    }

    public void AddDependent(IDependentCacheEntry? dependent)
    {
        if (dependent is not null)
            _dependents.Add(new WeakReference<IDependentCacheEntry>(dependent));
    }

    public void Invalidate()
    {
        _cache.TryRemove(_parameters, out _);

        foreach (var weakReference in _dependents)
        {
            if (weakReference.TryGetTarget(out var dependent))
                dependent.Invalidate();
        }
    }

}