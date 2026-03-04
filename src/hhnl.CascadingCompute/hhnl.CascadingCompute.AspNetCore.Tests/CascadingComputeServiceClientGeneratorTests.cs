using hhnl.CascadingCompute.AspNetCore.Client;
using hhnl.CascadingCompute.AspNetCore.Shared.Attributes;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Json;

namespace hhnl.CascadingCompute.AspNetCore.Tests;

[TestClass]
public class CascadingComputeServiceClientGeneratorTests
{
    [TestMethod]
    public void Generated_client_method_should_use_route_and_http_method_for_get_calls()
    {
        // Arrange
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(42)
            };
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost")
        };

        var client = new GeneratedWeatherClient(httpClient);

        // Act 1
        var result = client.GetForecast(10);

        // Assert 1
        Assert.AreEqual(42, result);
        Assert.IsNotNull(handler.LastRequestMethod);
        Assert.AreEqual(HttpMethod.Get, handler.LastRequestMethod);
        Assert.AreEqual("/api/weather/forecast/10", handler.LastRequestUri!.AbsolutePath);
    }

    [TestMethod]
    public async Task Generated_client_method_should_use_post_and_from_body_payload_for_marked_parameter()
    {
        // Arrange
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost")
        };

        var client = new GeneratedWeatherClient(httpClient);

        // Act 1
        await client.SetForecastAsync(10, 7, CancellationToken.None);

        // Assert 1
        Assert.IsNotNull(handler.LastRequestMethod);
        Assert.AreEqual(HttpMethod.Post, handler.LastRequestMethod);
        Assert.AreEqual("/api/weather/forecast/10", handler.LastRequestUri!.AbsolutePath);

        // Assert 2
        StringAssert.Contains(handler.LastRequestBody!, "7");
    }

    [TestMethod]
    public void Generated_client_method_should_use_default_route_and_query_parameters()
    {
        // Arrange
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create("pong")
            };
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost")
        };

        var client = new GeneratedWeatherClient(httpClient);

        // Act 1
        var result = client.Ping("berlin");

        // Assert 1
        Assert.AreEqual("pong", result);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/api/weather/Ping", handler.LastRequestUri!.AbsolutePath);

        // Assert 2
        Assert.AreEqual("?location=berlin", handler.LastRequestUri.Query);
    }
}

[CascadingComputeRoute("api/weather")]
public interface ITestWeatherClientService
{
    [CascadingComputeGet("forecast/{cityId:int}")]
    int GetForecast(int cityId);

    [CascadingComputePost("forecast/{cityId:int}")]
    Task SetForecastAsync(int cityId, [CascadingComputeRouteFromBody] int value, CancellationToken cancellationToken);

    string Ping(string location);
}

public partial class GeneratedWeatherClient(HttpClient httpClient) : CascadingComputeServiceClient<ITestWeatherClientService>(httpClient)
{
    [Obsolete]
    public partial int GetForecast(int cityId);
}

public sealed class TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
{
    public HttpMethod? LastRequestMethod { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestMethod = request.Method;
        LastRequestUri = request.RequestUri;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return await responseFactory(request, cancellationToken);
    }
}
