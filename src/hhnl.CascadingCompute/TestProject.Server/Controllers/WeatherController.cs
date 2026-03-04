using hhnl.CascadingCompute.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using TestProject.Server.Context;
using TestProject.Shared.Services;

namespace TestProject.Server.Controllers;

[ApiController]
[Route("api/weather")]
public partial class WeatherController(IWeatherService weatherService, TenantContextAccessor tenantContextAccessor)
    : CascadingComputeServiceController<IWeatherService>(weatherService)
{
    //[HttpGet("{cityId:int}")]
    //public ActionResult<int> GetForecast(int cityId)
    //    => Ok(weatherService.GetForecast(cityId));

    //[HttpPut("{cityId:int}")]
    //public async Task<IActionResult> SetForecast(int cityId, [FromBody] int value, CancellationToken cancellationToken)
    //{
    //    await weatherService.SetForecastAsync(cityId, value, cancellationToken);
    //    return NoContent();
    //}
}
