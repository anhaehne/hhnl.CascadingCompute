using hhnl.CascadingCompute.Shared.Attributes;
using TestProject.Server.Context;

namespace TestProject.Server.Services;

public interface IWeatherService
{
    int GetForecast(int cityId);
    Task SetForecastAsync(int cityId, int value, CancellationToken cancellationToken);
}

public sealed partial class WeatherService(
    WeatherDataStore weatherDataStore,
    TenantContextAccessor tenantContextAccessor) : IWeatherService
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
    }
}
