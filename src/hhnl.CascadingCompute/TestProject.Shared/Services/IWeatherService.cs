using hhnl.CascadingCompute.AspNetCore.Shared.Attributes;

namespace TestProject.Shared.Services;

[CascadingComputeRoute("/api/weather")]
public interface IWeatherService
{
    [CascadingComputeGet("{cityId:int}")]
    Task<int> GetForecastAsync(int cityId, CancellationToken cancellationToken);

    [CascadingComputePost("{cityId:int}")]
    Task SetForecastAsync(int cityId, [CascadingComputeRouteFromBody] int value, CancellationToken cancellationToken);
}
