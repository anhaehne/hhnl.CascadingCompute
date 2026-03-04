using hhnl.CascadingCompute.Shared.Attributes;
using TestProject.Server.Caching;
using TestProject.Server.Context;

namespace TestProject.Server.Services;

public sealed partial class WeatherService(
    WeatherDataStore weatherDataStore,
    TenantCacheContextProvider tenantCacheContextProvider,
    IInvalidationPublisher invalidationPublisher)
{
    [CascadingCompute]
    public int GetForecast(int cityId)
    {
        var baseValue = weatherDataStore.GetForecastBaseValue(cityId);
        return baseValue + 5;
    }

    public async Task SetForecastAsync(int cityId, int value, CancellationToken cancellationToken)
    {
        weatherDataStore.SetForecast(cityId, value);
        Invalidation.InvalidateGetForecast(cityId);

        await invalidationPublisher.PublishAsync(
            WeatherCacheKeyFactory.GetForecast(cityId),
            cancellationToken);
    }

    public string GetActiveTenantId()
        => tenantCacheContextProvider.GetCacheContext();
}
