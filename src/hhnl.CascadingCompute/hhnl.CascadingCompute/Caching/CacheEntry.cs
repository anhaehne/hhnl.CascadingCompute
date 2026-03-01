using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;
using System.Collections.Concurrent;

namespace hhnl.CascadingCompute.Caching;

public class CacheEntry<TParameters, TResult>(TParameters parameters, ConcurrentDictionary<TParameters, CacheEntry<TParameters, TResult>> cache, CacheEntryLifetimeObserverAttribute[] cacheEntryLifetimeObserverAttributes) : IDependentCacheEntry, ICacheEntry<TResult>
    where TParameters : notnull
{
    private List<WeakReference<IDependentCacheEntry>> _dependents = [];

    public TResult Value { get; set; } = default!;

    public void AddDependent(IDependentCacheEntry? dependent)
    {
        if (dependent is not null)
            _dependents.Add(new WeakReference<IDependentCacheEntry>(dependent));
    }

    public void Invalidate()
    {
        cache.TryRemove(parameters, out _);

        foreach (var weakReference in _dependents)
        {
            if (weakReference.TryGetTarget(out var dependent))
                dependent.Invalidate();
        }

        for (int i = 0; i < cacheEntryLifetimeObserverAttributes.Length; i++)
            cacheEntryLifetimeObserverAttributes[i].OnCacheEntryInvalidated(this);
    }

}