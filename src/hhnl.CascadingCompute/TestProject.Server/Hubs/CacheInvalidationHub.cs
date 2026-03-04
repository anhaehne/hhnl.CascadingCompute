using Microsoft.AspNetCore.SignalR;
using TestProject.Server.Context;
using TestProject.Server.Services;

namespace TestProject.Server.Hubs;

public sealed class CacheInvalidationHub(ITenantContextAccessor tenantContextAccessor) : Hub
{


    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRInvalidationPublisher.GetTenantGroupName(tenantContextAccessor.TenantId));
        await base.OnConnectedAsync();
    }
}
