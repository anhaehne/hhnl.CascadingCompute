using hhnl.CascadingCompute.AspNetCore.Client;
using hhnl.CascadingCompute.Shared.Attributes;
using TestProject.Shared.Services;

namespace TestProject.Client.Api;


public partial class WeatherApiClient(HttpClient httpClient)
    : CascadingComputeServiceClient<IWeatherService>(httpClient)
{
    [CascadingCompute]
    public partial Task<int> GetForecastAsync(int cityId, CancellationToken cancellationToken);
}
