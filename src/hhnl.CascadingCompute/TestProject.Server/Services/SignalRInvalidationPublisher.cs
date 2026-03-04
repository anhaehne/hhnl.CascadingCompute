using Microsoft.AspNetCore.SignalR;
using TestProject.Server.Contracts;
using TestProject.Server.Context;
using TestProject.Server.Hubs;

namespace TestProject.Server.Services;

public sealed class SignalRInvalidationPublisher(
    IHubContext<CacheInvalidationHub> hubContext,
    ITenantContextAccessor tenantContextAccessor) : IInvalidationPublisher
{
    private static long _sequence;

    public Task PublishAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContextAccessor.TenantId;
        var message = new CacheInvalidationMessage(
            cacheKey,
            new Dictionary<string, string>
            {
                ["tenant"] = tenantId
            },
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow);

        return hubContext.Clients.Group(GetTenantGroupName(tenantId)).SendAsync("cache-invalidated", message, cancellationToken);
    }

    public static string GetTenantGroupName(string tenantId)
        => $"tenant:{tenantId}";
}
