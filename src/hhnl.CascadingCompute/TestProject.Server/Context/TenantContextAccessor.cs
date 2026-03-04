using hhnl.CascadingCompute.Shared.Interfaces;

namespace TestProject.Server.Context;

public sealed class TenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ICacheContextProvider<string>
{
    public string TenantId => httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";

    public string GetCacheContext() => TenantId;
}
