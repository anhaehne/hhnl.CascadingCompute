using TestProject.Client.Api;
using TestProject.Client.Caching;
using TestProject.Client.SignalR;

var baseAddress = new Uri("https://localhost:5001/");
var tenantId = "tenant-a";

var cache = new ClientForecastCache();
var listener = new CacheInvalidationListener(cache);
await using var connection = listener.CreateConnection(new Uri(baseAddress, "hubs/cache-invalidation"));
await connection.StartAsync();

using var httpClient = new HttpClient
{
    BaseAddress = baseAddress
};

var weatherApiClient = new WeatherApiClient(httpClient, cache);

await weatherApiClient.SetForecastAsync(10, 20, tenantId, CancellationToken.None);
var first = await weatherApiClient.GetForecastAsync(10, tenantId, CancellationToken.None);
var second = await weatherApiClient.GetForecastAsync(10, tenantId, CancellationToken.None);

await weatherApiClient.SetForecastAsync(10, 30, tenantId, CancellationToken.None);
await Task.Delay(100, CancellationToken.None);
var third = await weatherApiClient.GetForecastAsync(10, tenantId, CancellationToken.None);

Console.WriteLine($"First={first}, Second={second}, Third={third}");
