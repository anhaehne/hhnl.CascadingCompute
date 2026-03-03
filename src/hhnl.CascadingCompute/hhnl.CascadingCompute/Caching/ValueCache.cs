using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;
using System.Collections.Concurrent;

namespace hhnl.CascadingCompute.Caching;


public class ValueCache<TService, TParameters, TResult>
    where TParameters : notnull
{

    private readonly ConcurrentDictionary<(TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints), CacheEntry<TParameters, TResult>> _entries
        = new(new CacheKeyEqualityComparer());

    public TResult GetOrAdd(
        TService service,
        TParameters parameters,
        Func<TService, TParameters, TResult> valueFactory,
        CacheEntryLifetimeObserverAttribute[] cacheEntryLifetimeObserverAttributes,
        (string Key, object Value)[] taints)
    {
        var parentEntry = CacheDependencyContext.CurrentEntry.Value;
        var parentTaints = CacheDependencyContext.CurrentTaints.Value;

        IReadOnlyCollection<(string Key, object Value)> combineTaints = taints;

        if (parentTaints is { Count: > 0 })
        {
            if (taints.Length == 0)
            {
                combineTaints = parentTaints;
            }
            else
            {
                var combinedTaints = new EquatableSet<(string Key, object Value)>(taints);
                combinedTaints.UnionWith(parentTaints);
                combineTaints = combinedTaints;
            }
        }

        if (_entries.TryGetValue((parameters, combineTaints), out var entry))
        {
            entry.AddDependent(parentEntry);
            return entry.Value;
        }

        lock (_entries)
        {
            if (_entries.TryGetValue((parameters, combineTaints), out var innerEntry))
            {
                innerEntry.AddDependent(parentEntry);
                return innerEntry.Value;
            }

            var newEntry = new CacheEntry<TParameters, TResult>(parameters, _entries, cacheEntryLifetimeObserverAttributes, taints);
            newEntry.AddDependent(parentEntry);

            CacheDependencyContext.CurrentEntry.Value = newEntry;
            newEntry.Value = valueFactory(service, parameters);
            CacheDependencyContext.CurrentEntry.Value = parentEntry;

            _entries[(parameters, newEntry.Taints)] = newEntry;

            for (int i = 0; i < cacheEntryLifetimeObserverAttributes.Length; i++)
                cacheEntryLifetimeObserverAttributes[i].OnCacheEntryCreated(newEntry);

            return newEntry.Value;
        }
    }

    public void Invalidate(TParameters parameters, (string Key, object Value)[] taints, Action<ICacheEntry<TResult>>? onCacheEntryInvalidated = null)
        => Invalidate((parameters, taints), onCacheEntryInvalidated);

    public void InvalidateAll(Action<ICacheEntry<TResult>>? onCacheEntryInvalidated = null)
    {
        foreach (var key in _entries.Keys)
            Invalidate(key, onCacheEntryInvalidated);
    }

    public void InvalidateWhere(Func<TParameters, bool> predicate, Action<ICacheEntry<TResult>>? onCacheEntryInvalidated = null)
    {
        foreach (var key in _entries.Keys)
        {
            if (!predicate(key.Parameters))
                continue;

            Invalidate(key, onCacheEntryInvalidated);
        }
    }

    public void Invalidate((TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints) key, Action<ICacheEntry<TResult>>? onCacheEntryInvalidated = null)
    {
        if (_entries.TryRemove(key, out var entry))
        {
            entry.Invalidate();
            onCacheEntryInvalidated?.Invoke(entry);
        }
    }

    private class CacheKeyEqualityComparer : IEqualityComparer<(TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints)>
    {
        public bool Equals((TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints) x, (TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints) y)
        {
            if (!EqualityComparer<TParameters>.Default.Equals(x.Parameters, y.Parameters))
                return false;

            if (x.Taints is not EquatableSet<(string Key, object Value)> taints)
                throw new InvalidOperationException("Unexpected key in dictionary.");

            return taints.IsSubsetOf(y.Taints);
        }
        public int GetHashCode((TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints) obj)
        {
            int hash = EqualityComparer<TParameters>.Default.GetHashCode(obj.Parameters);
            foreach (var taint in obj.Taints)
            {
                hash = HashCode.Combine(hash, taint.Key.GetHashCode(), taint.Value?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}