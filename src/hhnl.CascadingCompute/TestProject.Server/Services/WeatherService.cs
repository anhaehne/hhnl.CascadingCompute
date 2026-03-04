using hhnl.CascadingCompute.Shared.Attributes;
using TestProject.Server.Context;
using TestProject.Shared.Services;

namespace TestProject.Server.Services;

public sealed partial class WeatherService(
    WeatherDataStore weatherDataStore,
    TenantContextAccessor tenantContextAccessor) : IWeatherService
{
    [CascadingCompute]
    public async Task<int> GetForecastAsync(int cityId, CancellationToken cancellationToken)
    {
        var baseValue = weatherDataStore.GetForecastBaseValue(cityId);
        return baseValue + 5;
    }

    public async Task SetForecastAsync(int cityId, int value, CancellationToken cancellationToken)
    {
        weatherDataStore.SetForecast(cityId, value);
        Invalidation.InvalidateGetForecastAsync(cityId);
    }
}
