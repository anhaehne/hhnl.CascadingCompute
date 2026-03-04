using Microsoft.AspNetCore.SignalR.Client;
using TestProject.Client.Caching;
using TestProject.Client.Contracts;

namespace TestProject.Client.SignalR;

public sealed class CacheInvalidationListener(ClientForecastCache cache)
{
    public HubConnection CreateConnection(Uri hubUri)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect()
            .Build();

        connection.On<CacheInvalidationMessage>("cache-invalidated", message =>
        {
            if (message.Taints.TryGetValue("tenant", out var tenantId))
                cache.Invalidate(message.CacheKey, tenantId);
        });

        return connection;
    }
}
