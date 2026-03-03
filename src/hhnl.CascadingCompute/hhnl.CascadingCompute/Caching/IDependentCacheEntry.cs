namespace hhnl.CascadingCompute.Caching;

public interface IDependentCacheEntry
{
    void Invalidate();

    void AddTaint((string Key, object Value) taint);
}