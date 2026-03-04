using hhnl.CascadingCompute.AspNetCore.Shared.Attributes;
using hhnl.CascadingCompute.AspNetCore.Shared.Enums;

namespace TestProject.Shared.Services;

public interface IWeatherService
{
    [CascadingComputeRoute("forecast/{cityId:int}")]
    int GetForecast(int cityId);

    [CascadingComputeRoute("forecast/{cityId:int}", CascadingComputeHttpMethod.Post)]
    Task SetForecastAsync(int cityId, [CascadingComputeRouteFromBody] int value, CancellationToken cancellationToken);
}
