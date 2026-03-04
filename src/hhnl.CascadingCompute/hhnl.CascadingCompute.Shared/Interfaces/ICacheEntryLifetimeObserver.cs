namespace hhnl.CascadingCompute.Shared.Interfaces;

public interface ICacheEntryLifetimeObserver
{
    void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry);

    void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry);
}

