using TestProject.Client.Api;

var baseAddress = new Uri("https://localhost:57787/");


using var httpClient = new HttpClient
{
    BaseAddress = baseAddress
};

var weatherApiClient = new WeatherApiClient(httpClient);

weatherApiClient.Start();

await weatherApiClient.SetForecastAsync(10, 20, CancellationToken.None);

var first = await weatherApiClient.GetForecastAsync(10, CancellationToken.None);
var second = await weatherApiClient.GetForecastAsync(10, CancellationToken.None);

await Task.Delay(20);

await weatherApiClient.SetForecastAsync(10, 30, CancellationToken.None);

Console.ReadLine();

var third = await weatherApiClient.GetForecastAsync(10, CancellationToken.None);

Console.WriteLine($"First={first}, Second={second}, Third={third}");

await Task.Delay(TimeSpan.FromDays(1));
