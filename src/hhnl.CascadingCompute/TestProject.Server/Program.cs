using TestProject.Server.Context;
using TestProject.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TenantContextAccessor>();
builder.Services.AddSingleton<WeatherDataStore>();
builder.Services.AddSingleton<IWeatherService, WeatherService>();

builder.Services.EnableCascadingCompute();

var app = builder.Build();

app.MapControllers();

app.Run();
