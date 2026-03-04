using hhnl.CascadingCompute.AspNetCore.Shared.Attributes;

namespace TestProject.Shared.Services;

[CascadingComputeRoute("/api/weather")]
public interface IWeatherService
{
    [CascadingComputeGet("{cityId:int}")]
    int GetForecast(int cityId);

    [CascadingComputePost("{cityId:int}")]
    Task SetForecastAsync(int cityId, [CascadingComputeRouteFromBody] int value, CancellationToken cancellationToken);
}
