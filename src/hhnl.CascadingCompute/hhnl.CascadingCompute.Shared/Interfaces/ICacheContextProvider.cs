namespace hhnl.CascadingCompute.Shared.Interfaces;

/// <summary>
/// Allows to provide a context for cache entries, which can be used to make cache entries more specific.
/// One example of a cache context is a user ID, which can be used to make cache entries specific to a user.
/// </summary>
public interface ICacheContextProvider<TContext>
{
    TContext GetCacheContext();
}