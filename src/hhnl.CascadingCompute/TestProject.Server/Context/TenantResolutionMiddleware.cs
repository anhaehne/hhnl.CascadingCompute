namespace TestProject.Server.Context;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        tenantContextAccessor.TenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
        await next(context);
    }
}
