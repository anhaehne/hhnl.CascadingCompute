using hhnl.CascadingCompute.AspNetCore.Shared.Attributes;
using hhnl.CascadingCompute.AspNetCore.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace hhnl.CascadingCompute.AspNetCore.Tests;

[TestClass]
public class CascadingComputeServiceControllerGeneratorTests
{
    [TestMethod]
    public async Task Generated_controller_method_should_delegate_to_service_method()
    {
        // Arrange
        var service = new TestWeatherService();
        var controller = new GeneratedWeatherController(service);

        // Act 1
        var forecast = controller.GetForecast(10);

        // Assert 1
        Assert.AreEqual(42, forecast);
        Assert.AreEqual(1, service.GetForecastCallCount);

        // Act 2
        await controller.SetForecastAsync(10, 7, CancellationToken.None);

        // Assert 2
        Assert.AreEqual(1, service.SetForecastCallCount);
        Assert.AreEqual(10, service.LastSetCityId);
        Assert.AreEqual(7, service.LastSetValue);
    }

    [TestMethod]
    public void Generated_controller_method_should_use_route_attribute_template_with_get_http_method()
    {
        // Arrange
        var method = typeof(GeneratedWeatherController).GetMethod(nameof(GeneratedWeatherController.GetForecast));

        // Act 1
        var httpGetAttribute = method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Cast<HttpGetAttribute>().Single();

        // Assert 1
        Assert.AreEqual("forecast/{cityId:int}", httpGetAttribute.Template);
    }

    [TestMethod]
    public void Generated_controller_method_should_use_default_route_when_route_attribute_is_missing()
    {
        // Arrange
        var method = typeof(GeneratedWeatherController).GetMethod(nameof(GeneratedWeatherController.Ping));

        // Act 1
        var httpGetAttribute = method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Cast<HttpGetAttribute>().Single();

        // Assert 1
        Assert.AreEqual(nameof(GeneratedWeatherController.Ping), httpGetAttribute.Template);
    }

    [TestMethod]
    public void Generated_controller_method_should_apply_from_body_attribute_for_marked_parameter()
    {
        // Arrange
        var method = typeof(GeneratedWeatherController).GetMethod(nameof(GeneratedWeatherController.SetForecastAsync));
        var parameters = method!.GetParameters();

        // Act 1
        var valueParameter = parameters.Single(parameter => parameter.Name == "value");

        // Assert 1
        Assert.IsTrue(valueParameter.GetCustomAttributes(typeof(FromBodyAttribute), inherit: true).Any());
    }

    [TestMethod]
    public void Generated_controller_method_should_generate_correct_http_method_attribute()
    {
        // Arrange
        var method = typeof(GeneratedWeatherController).GetMethod(nameof(GeneratedWeatherController.SetForecastAsync));

        // Act 1
        var httpGetAttributes = method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Cast<HttpGetAttribute>().ToList();
        var httpPostAttributes = method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Cast<HttpPostAttribute>().ToList();

        // Assert 1
        Assert.IsEmpty(httpGetAttributes);
        Assert.HasCount(1, httpPostAttributes);
    }
}

public interface ITestWeatherService
{
    [CascadingComputeRoute("forecast/{cityId:int}")]
    int GetForecast(int cityId);

    [CascadingComputeRoute("forecast/{cityId:int}", CascadingComputeHttpMethod.Post)]
    Task SetForecastAsync(int cityId, [CascadingComputeRouteFromBody] int value, CancellationToken cancellationToken);

    [CascadingComputeRoute("forecast/{cityId:int}", CascadingComputeHttpMethod.Put)]
    Task UpdateForecast(int cityId, [CascadingComputeRouteFromBody] int value, CancellationToken cancellationToken);

    [CascadingComputeRoute("forecast/{cityId:int}", CascadingComputeHttpMethod.Delete)]
    Task DeleteForecast(int cityId, CancellationToken cancellationToken);

    string Ping(string location);
}

public sealed class TestWeatherService : ITestWeatherService
{
    public int GetForecastCallCount { get; private set; }
    public int SetForecastCallCount { get; private set; }
    public int LastSetCityId { get; private set; }
    public int LastSetValue { get; private set; }

    public int GetForecast(int cityId)
    {
        GetForecastCallCount++;
        return 42;
    }

    public Task SetForecastAsync(int cityId, int value, CancellationToken cancellationToken)
    {
        SetForecastCallCount++;
        LastSetCityId = cityId;
        LastSetValue = value;
        return Task.CompletedTask;
    }

    public Task UpdateForecast(int cityId, int value, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeleteForecast(int cityId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public string Ping(string location) => $"pong:{location}";
}

public partial class GeneratedWeatherController(TestWeatherService service) : CascadingComputeServiceController<ITestWeatherService>(service)
{
}
