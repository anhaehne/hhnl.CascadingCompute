using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;
using System.Collections.Concurrent;

namespace hhnl.CascadingCompute.Caching;

public class CacheEntry<TParameters, TResult>(
    TParameters parameters,
    ConcurrentDictionary<(TParameters Parameters, IReadOnlyCollection<(string Key, object Value)> Taints), CacheEntry<TParameters, TResult>> cache,
    CacheEntryLifetimeObserverAttribute[] cacheEntryLifetimeObserverAttributes,
    (string, object)[] taints) : IDependentCacheEntry, ICacheEntry<TResult>
    where TParameters : notnull
{
    private List<WeakReference<IDependentCacheEntry>> _dependents = [];
    private EquatableSet<(string, object)> _taints = new(taints);

    public TResult Value { get; set; } = default!;

    public IReadOnlySet<(string Key, object Value)> Taints => _taints;


    public void AddDependent(IDependentCacheEntry? dependent)
    {
        if (dependent is null)
            return;

        _dependents.Add(new WeakReference<IDependentCacheEntry>(dependent));
        foreach (var taint in _taints)
            dependent.AddTaint(taint);
    }

    public void AddTaint((string, object) taint)
    {
        _taints.Add(taint);
        foreach (var dependent in _dependents)
        {
            if (dependent.TryGetTarget(out var d))
                d.AddTaint(taint);
        }
    }

    public void Invalidate()
    {
        cache.TryRemove((parameters, _taints.ToArray()), out _);

        foreach (var weakReference in _dependents)
        {
            if (weakReference.TryGetTarget(out var dependent))
                dependent.Invalidate();
        }

        for (int i = 0; i < cacheEntryLifetimeObserverAttributes.Length; i++)
            cacheEntryLifetimeObserverAttributes[i].OnCacheEntryInvalidated(this);
    }

}