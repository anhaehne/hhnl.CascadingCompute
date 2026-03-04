namespace TestProject.Server.Services;

public sealed class WeatherDataStore
{
    private readonly Dictionary<int, int> _cityForecasts = new();

    public int GetForecastBaseValue(int cityId)
        => _cityForecasts.TryGetValue(cityId, out var value) ? value : 0;

    public void SetForecast(int cityId, int value)
        => _cityForecasts[cityId] = value;
}
