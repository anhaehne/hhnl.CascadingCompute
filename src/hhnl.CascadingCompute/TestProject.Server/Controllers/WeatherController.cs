using hhnl.CascadingCompute.AspNetCore;
using TestProject.Server.Context;
using TestProject.Shared.Services;

namespace TestProject.Server.Controllers;

public partial class WeatherController(IWeatherService weatherService, TenantContextAccessor tenantContextAccessor)
    : CascadingComputeServiceController<IWeatherService>(weatherService)
{
}
