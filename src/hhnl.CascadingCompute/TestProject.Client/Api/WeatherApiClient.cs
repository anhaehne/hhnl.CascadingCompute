using System.Net.Http.Json;
using TestProject.Client.Caching;

namespace TestProject.Client.Api;

public sealed class WeatherApiClient(HttpClient httpClient, ClientForecastCache cache)
{
    public async Task<int> GetForecastAsync(int cityId, string tenantId, CancellationToken cancellationToken)
    {
        var cacheKey = WeatherCacheKeyFactory.GetForecast(cityId);
        if (cache.TryGet(cacheKey, tenantId, out var cachedValue))
            return cachedValue;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/weather/{cityId}");
        request.Headers.Add("X-Tenant-Id", tenantId);

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var value = await response.Content.ReadFromJsonAsync<int>(cancellationToken: cancellationToken);
        cache.Set(cacheKey, tenantId, value);

        return value;
    }

    public async Task SetForecastAsync(int cityId, int value, string tenantId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/weather/{cityId}")
        {
            Content = JsonContent.Create(value)
        };
        request.Headers.Add("X-Tenant-Id", tenantId);

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
