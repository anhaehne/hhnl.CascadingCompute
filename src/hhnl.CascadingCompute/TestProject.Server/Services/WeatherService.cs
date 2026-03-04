using hhnl.CascadingCompute.Shared.Attributes;
using TestProject.Server.Context;
using TestProject.Shared.Services;

namespace TestProject.Server.Services;

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
