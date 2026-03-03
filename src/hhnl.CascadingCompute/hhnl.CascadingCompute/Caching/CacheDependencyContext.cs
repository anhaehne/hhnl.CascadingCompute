namespace hhnl.CascadingCompute.Caching;

public static class CacheDependencyContext
{
    public static AsyncLocal<IDependentCacheEntry?> CurrentEntry = new();

    public static AsyncLocal<IReadOnlyCollection<(string Key, object Value)>?> CurrentTaints = new();
}
