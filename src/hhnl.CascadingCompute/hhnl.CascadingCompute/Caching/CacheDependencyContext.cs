namespace hhnl.CascadingCompute.Caching;

public static class CacheDependencyContext
{
    public static AsyncLocal<IDependentCacheEntry?> Current = new();
}
