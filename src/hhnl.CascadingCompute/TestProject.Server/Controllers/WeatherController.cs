using hhnl.CascadingCompute.AspNetCore;
using TestProject.Server.Context;
using TestProject.Shared.Services;

namespace TestProject.Server.Controllers;

public partial class WeatherController(IWeatherService weatherService, TenantContextAccessor tenantContextAccessor)
    : CascadingComputeServiceController<IWeatherService>(weatherService)
{

    //[HttpPut("{cityId:int}")]
    //public async Task<IActionResult> SetForecast(int cityId, [FromBody] int value, CancellationToken cancellationToken)
    //{
    //    await weatherService.SetForecastAsync(cityId, value, cancellationToken);
    //    return NoContent();
    //}
}
