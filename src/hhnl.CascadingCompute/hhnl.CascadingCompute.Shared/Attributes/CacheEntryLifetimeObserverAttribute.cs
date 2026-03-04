using hhnl.CascadingCompute.Shared.Interfaces;

namespace hhnl.CascadingCompute.Shared.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public abstract class CacheEntryLifetimeObserverAttribute() : Attribute, ICacheEntryLifetimeObserver
{
    public abstract void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry);

    public abstract void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry);
}
