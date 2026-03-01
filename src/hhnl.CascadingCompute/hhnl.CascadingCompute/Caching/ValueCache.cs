using hhnl.CascadingCompute.Shared.Interfaces;
using System.Collections.Concurrent;

namespace hhnl.CascadingCompute.Caching;


public class ValueCache<TService, TParameters, TResult>
    where TParameters : notnull
{
    private readonly ConcurrentDictionary<TParameters, CacheEntry<TParameters, TResult>> _entries = new();

    public TResult GetOrAdd(TService service, TParameters parameters, Func<TService, TParameters, TResult> valueFactory, Action<ICacheEntry<TResult>>? onCacheEntryCreated = null)
    {
        var dependent = CacheDependencyContext.Current.Value;

        if (_entries.TryGetValue(parameters, out var entry))
        {
            entry.AddDependent(dependent);
            return entry.Value;
        }

        lock (_entries)
        {
            if (_entries.TryGetValue(parameters, out var innerEntry))
            {
                innerEntry.AddDependent(dependent);
                return innerEntry.Value;
            }

            var newEntry = new CacheEntry<TParameters, TResult>(parameters, _entries);
            newEntry.AddDependent(dependent);

            CacheDependencyContext.Current.Value = newEntry;
            newEntry.Value = valueFactory(service, parameters);
            CacheDependencyContext.Current.Value = dependent;

            _entries[parameters] = newEntry;
            onCacheEntryCreated?.Invoke(newEntry);
            return newEntry.Value;
        }
    }

    public void Invalidate(TParameters parameters, Action<ICacheEntry<TResult>>? onCacheEntryInvalidated = null)
    {
        if (_entries.TryRemove(parameters, out var entry))
        {
            entry.Invalidate();
            onCacheEntryInvalidated?.Invoke(entry);
        }
    }

    public void InvalidateAll(Action<ICacheEntry<TResult>>? onCacheEntryInvalidated = null)
    {
        foreach (var key in _entries.Keys)
            Invalidate(key, onCacheEntryInvalidated);
    }
}