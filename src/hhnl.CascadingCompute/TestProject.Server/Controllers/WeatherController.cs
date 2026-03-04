using hhnl.CascadingCompute.AspNetCore;
using hhnl.CascadingCompute.AspNetCore.Interfaces;
using hhnl.CascadingCompute.AspNetCore.Shared.Models;
using hhnl.CascadingCompute.AspNetCore.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TestProject.Server.Context;
using TestProject.Server.Services;

namespace TestProject.Server.Controllers;

[ApiController]
[Route("api/weather")]
[TypeFilter<CascadingComputeActionFilter<WeatherController>>]
public class WeatherController(WeatherService weatherService, TenantCacheContextProvider tenantCacheContextProvider) : ControllerBase, ICascadingComputeController
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

    public IReadOnlyCollection<(string Key, object Value)> GetCacheContext()
            => [("global::TestProject.Server.Context.TenantCacheContextProvider|string", tenantCacheContextProvider.GetCacheContext())];

    [HttpGet("invalidations")]
    public async Task<ServerSentEventsResult<InvalidationDto>> InvalidationsAsync(CancellationToken cancellationToken)
    {
        return TypedResults.ServerSentEvents(Utils.StreamInvalidationEvents<WeatherController>(GetCacheContext(), cancellationToken), "invalidations");
    }

    public static void OnCacheEntryInvalidated(string url, IReadOnlyCollection<(string Key, object Value)> taints)
        => CacheEntryInvalidated?.Invoke(null, (url, taints));

    public static event EventHandler<(string url, IReadOnlyCollection<(string Key, object Value)> taints)>? CacheEntryInvalidated;
}
