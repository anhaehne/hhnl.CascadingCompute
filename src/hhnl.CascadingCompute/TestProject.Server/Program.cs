using TestProject.Server.Context;
using TestProject.Server.Hubs;
using TestProject.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddSingleton<TenantCacheContextProvider>();
builder.Services.AddSingleton<WeatherDataStore>();
builder.Services.AddSingleton<WeatherService>();
builder.Services.AddSingleton<IInvalidationPublisher, SignalRInvalidationPublisher>();

var app = builder.Build();

app.UseMiddleware<TenantResolutionMiddleware>();
app.MapControllers();
app.MapHub<CacheInvalidationHub>("/hubs/cache-invalidation");

app.Run();
