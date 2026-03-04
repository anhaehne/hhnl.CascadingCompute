using hhnl.CascadingCompute.Shared.Interfaces;

namespace TestProject.Server.Context;

public sealed class TenantCacheContextProvider(ITenantContextAccessor tenantContextAccessor) : ICacheContextProvider<string>
{
    public string GetCacheContext() => tenantContextAccessor.TenantId;
}
