namespace TestProject.Client.Caching;

public static class WeatherCacheKeyFactory
{
    public static string GetForecast(int cityId)
        => $"WeatherService:GetForecast:{cityId}";
}
