using hhnl.CascadingCompute.AspNetCore.Interfaces;
using hhnl.CascadingCompute.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;

namespace hhnl.CascadingCompute.AspNetCore;

public class CascadingComputeActionFilter<TController>() : IAsyncActionFilter
    where TController : ControllerBase, ICascadingComputeController
{
    private readonly static ConcurrentDictionary<Entry, object?> _entries = new(new Entry.CacheKeyEqualityComparer());

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var entry = new Entry(context.HttpContext.Request.Path, _entries);
        CacheDependencyContext.CurrentEntry.Value = entry;
        CacheDependencyContext.CurrentTaints.Value = ((ICascadingComputeController)context.Controller).GetCacheContext();

        await next();

        // We only have to track entries with dependencies, as those are the only ones that can cause invalidations.
        if (!entry.HasDependencies)
            return;

        if (_entries.TryAdd(entry, null))
        {
            // New entry;
        }
    }

    private class Entry(string url, ConcurrentDictionary<Entry, object?> entries) : IDependentCacheEntry
    {
        private readonly EquatableSet<(string, object)> _taints = [];

        public string Url { get; } = url;

        public bool HasDependencies { get; private set; }

        public void AddTaint((string Key, object Value) taint)
        {
            _taints.Add(taint);
        }

        public void Invalidate()
        {
            entries.Remove(this, out _);
            TController.OnCacheEntryInvalidated(Url, _taints);
        }

        public void OnDependencyAdded(IDependentCacheEntry dependency)
            => HasDependencies = true;

        public class CacheKeyEqualityComparer : IEqualityComparer<Entry>
        {
            public bool Equals(Entry? x, Entry? y)
            {
                if (x is null && y is null)
                    return true;

                if (x is null || y is null)
                    return false;

                return x._taints.SetEquals(y._taints) && x.Url == y.Url;
            }

            public int GetHashCode(Entry obj) => HashCode.Combine(obj._taints, obj.Url);
        }
    }
}