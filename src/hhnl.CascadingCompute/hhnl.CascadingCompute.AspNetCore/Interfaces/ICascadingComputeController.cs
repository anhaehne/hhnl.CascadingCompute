namespace hhnl.CascadingCompute.AspNetCore.Interfaces;

public interface ICascadingComputeController
{
    IReadOnlyCollection<(string Key, object Value)> GetCacheContext();

    static abstract void OnCacheEntryInvalidated(string url, IReadOnlyCollection<(string Key, object Value)> taints);

    static abstract event EventHandler<(string url, IReadOnlyCollection<(string Key, object Value)> taints)>? CacheEntryInvalidated;
}