namespace TestProject.Server.Caching;

public static class WeatherCacheKeyFactory
{
    public static string GetForecast(int cityId)
        => $"WeatherService:GetForecast:{cityId}";
}
