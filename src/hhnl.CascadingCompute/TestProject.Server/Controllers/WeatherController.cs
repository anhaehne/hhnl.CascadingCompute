using hhnl.CascadingCompute.AspNetCore.Shared.Attributes;
using Microsoft.AspNetCore.Mvc;
using TestProject.Server.Context;
using TestProject.Shared.Services;

namespace TestProject.Server.Controllers;

[ApiController]
[Route("api/weather")]
[CascadingComputeController]
public partial class WeatherController(IWeatherService weatherService, TenantContextAccessor tenantContextAccessor) : ControllerBase
{
    [HttpGet("{cityId:int}")]
    public ActionResult<int> GetForecast(int cityId)
        => Ok(weatherService.GetForecast(cityId));

    [HttpPut("{cityId:int}")]
    public async Task<IActionResult> SetForecast(int cityId, [FromBody] int value, CancellationToken cancellationToken)
    {
        await weatherService.SetForecastAsync(cityId, value, cancellationToken);
        return NoContent();
    }
}
