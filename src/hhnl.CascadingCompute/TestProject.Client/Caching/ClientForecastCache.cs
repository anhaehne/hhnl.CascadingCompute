namespace TestProject.Client.Caching;

public sealed class ClientForecastCache
{
    private readonly Dictionary<(string CacheKey, string TenantId), int> _entries = new();

    public bool TryGet(string cacheKey, string tenantId, out int value)
        => _entries.TryGetValue((cacheKey, tenantId), out value);

    public void Set(string cacheKey, string tenantId, int value)
        => _entries[(cacheKey, tenantId)] = value;

    public void Invalidate(string cacheKey, string tenantId)
        => _entries.Remove((cacheKey, tenantId));
}
