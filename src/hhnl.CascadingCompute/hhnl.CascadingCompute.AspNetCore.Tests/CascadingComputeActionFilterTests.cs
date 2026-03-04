using hhnl.CascadingCompute.AspNetCore.Attributes;
using hhnl.CascadingCompute.Caching;
using hhnl.CascadingCompute.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Reflection;
using System.Text;

namespace hhnl.CascadingCompute.AspNetCore.Tests;

[TestClass]
[DoNotParallelize]
public partial class CascadingComputeActionFilterTests
{
    [TestMethod]
    public async Task OnActionExecutionAsync_should_set_cache_context_for_current_execution()
    {
        // Arrange
        ResetEntries();
        TestController.CurrentTenant = "tenant-a";
        var filter = new CascadingComputeActionFilter<TestController>();
        var controller = new TestController();
        var context = CreateActionExecutingContext(controller, "/api/weather/10");

        IReadOnlyCollection<(string Key, object Value)>? observedTaints = null;
        IDependentCacheEntry? observedEntry = null;

        ActionExecutionDelegate next = () =>
        {
            observedTaints = CacheDependencyContext.CurrentTaints.Value;
            observedEntry = CacheDependencyContext.CurrentEntry.Value;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller));
        };

        // Act 1
        await filter.OnActionExecutionAsync(context, next);

        // Assert 1
        Assert.IsNotNull(observedTaints);
        Assert.HasCount(1, observedTaints);
        StringAssert.EndsWith(observedTaints.First().Key, "TestTenantCacheContextProvider|string");
        Assert.AreEqual("tenant-a", observedTaints.First().Value);

        // Assert 2
        Assert.IsNotNull(observedEntry);
    }

    [TestMethod]
    public async Task OnActionExecutionAsync_should_not_track_entry_without_dependencies()
    {
        // Arrange
        ResetEntries();
        TestController.CurrentTenant = "tenant-a";
        var filter = new CascadingComputeActionFilter<TestController>();
        var controller = new TestController();
        var context = CreateActionExecutingContext(controller, "/api/weather/10");

        ActionExecutionDelegate next = ()
            => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller));

        // Act 1
        await filter.OnActionExecutionAsync(context, next);

        // Assert 1
        Assert.AreEqual(0, GetEntryCount());
    }

    [TestMethod]
    public async Task OnActionExecutionAsync_should_track_single_entry_for_same_url_and_taints()
    {
        // Arrange
        ResetEntries();
        TestController.CurrentTenant = "tenant-a";
        var filter = new CascadingComputeActionFilter<TestController>();
        var controller = new TestController();
        var firstContext = CreateActionExecutingContext(controller, "/api/weather/10");
        var secondContext = CreateActionExecutingContext(controller, "/api/weather/10");

        ActionExecutionDelegate nextWithDependency = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(firstContext, new List<IFilterMetadata>(), controller));
        };

        ActionExecutionDelegate nextWithDependencyAgain = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(secondContext, new List<IFilterMetadata>(), controller));
        };

        // Act 1
        await filter.OnActionExecutionAsync(firstContext, nextWithDependency);

        // Assert 1
        Assert.AreEqual(1, GetEntryCount());

        // Act 2
        await filter.OnActionExecutionAsync(secondContext, nextWithDependencyAgain);

        // Assert 2
        Assert.AreEqual(1, GetEntryCount());
    }

    [TestMethod]
    public async Task OnActionExecutionAsync_should_track_entries_for_different_urls_when_dependencies_exist()
    {
        // Arrange
        ResetEntries();
        TestController.CurrentTenant = "tenant-a";
        var filter = new CascadingComputeActionFilter<TestController>();
        var controller = new TestController();
        var firstContext = CreateActionExecutingContext(controller, "/api/weather/10");
        var secondContext = CreateActionExecutingContext(controller, "/api/weather/11");

        ActionExecutionDelegate nextWithDependencyForFirst = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(firstContext, new List<IFilterMetadata>(), controller));
        };

        ActionExecutionDelegate nextWithDependencyForSecond = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(secondContext, new List<IFilterMetadata>(), controller));
        };

        // Act 1
        await filter.OnActionExecutionAsync(firstContext, nextWithDependencyForFirst);

        // Act 2
        await filter.OnActionExecutionAsync(secondContext, nextWithDependencyForSecond);

        // Assert 1
        Assert.AreEqual(2, GetEntryCount());
    }

    [TestMethod]
    public async Task OnActionExecutionAsync_should_track_entries_for_different_taints_when_dependencies_exist()
    {
        // Arrange
        ResetEntries();
        var filter = new CascadingComputeActionFilter<TestController>();
        var controller = new TestController();
        var firstContext = CreateActionExecutingContext(controller, "/api/weather/10");
        var secondContext = CreateActionExecutingContext(controller, "/api/weather/10");

        ActionExecutionDelegate nextWithDependencyForFirst = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(firstContext, new List<IFilterMetadata>(), controller));
        };

        ActionExecutionDelegate nextWithDependencyForSecond = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(secondContext, new List<IFilterMetadata>(), controller));
        };

        // Act 1
        TestController.CurrentTenant = "tenant-a";
        await filter.OnActionExecutionAsync(firstContext, nextWithDependencyForFirst);

        // Act 2
        TestController.CurrentTenant = "tenant-b";
        await filter.OnActionExecutionAsync(secondContext, nextWithDependencyForSecond);

        // Assert 1
        Assert.AreEqual(2, GetEntryCount());
    }

    [TestMethod]
    public async Task Entry_should_remove_entry_and_raise_event_on_invalidate()
    {
        // Arrange
        ResetEntries();
        TestController.CurrentTenant = "tenant-a";
        var filter = new CascadingComputeActionFilter<TestController>();
        var controller = new TestController();
        var context = CreateActionExecutingContext(controller, "/api/weather/10");

        (string url, IReadOnlyCollection<(string Key, object Value)> taints)? observedInvalidation = null;
        EventHandler<(string url, IReadOnlyCollection<(string Key, object Value)> taints)> handler = (_, args) => observedInvalidation = args;
        TestController.CacheEntryInvalidated += handler;

        ActionExecutionDelegate nextWithDependency = () =>
        {
            CacheDependencyContext.CurrentEntry.Value!.OnDependencyAdded(new StubDependentCacheEntry());
            AddCurrentTaintsToEntry();
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller));
        };

        try
        {
            // Act 1
            await filter.OnActionExecutionAsync(context, nextWithDependency);

            // Assert 1
            Assert.AreEqual(1, GetEntryCount());

            // Act 2
            InvalidateFirstEntry();

            // Assert 2
            Assert.IsNotNull(observedInvalidation);
            Assert.AreEqual("/api/weather/10", observedInvalidation.Value.url);
            StringAssert.EndsWith(observedInvalidation.Value.taints.First().Key, "TestTenantCacheContextProvider|string");
            Assert.AreEqual("tenant-a", observedInvalidation.Value.taints.First().Value);

            // Assert 3
            Assert.AreEqual(0, GetEntryCount());
        }
        finally
        {
            TestController.CacheEntryInvalidated -= handler;
        }
    }

    [TestMethod]
    public async Task InvalidationsAsync_should_stream_invalidation_event_for_matching_cache_context()
    {
        // Arrange
        TestController.CurrentTenant = "tenant-a";
        var controller = new TestController();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = responseBody;
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();

        // Act 1
        var result = await controller.InvalidationsAsync(cancellationTokenSource.Token);
        var executeTask = result.ExecuteAsync(httpContext);

        // Act 2
        await Task.Delay(100, cancellationTokenSource.Token);
        TestController.OnCacheEntryInvalidated(
            "/api/weather/10",
            [("global::hhnl.CascadingCompute.AspNetCore.Tests.CascadingComputeActionFilterTests.TestTenantCacheContextProvider|string", "tenant-a")]);

        // Act 3
        await Task.Delay(200, cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
        }

        var responsePayload = Encoding.UTF8.GetString(responseBody.ToArray());

        // Assert 1
        StringAssert.Contains(responsePayload, "/api/weather/10");

        // Assert 2
        StringAssert.Contains(responsePayload, "tenant-a");
    }

    private static ActionExecutingContext CreateActionExecutingContext(TestController controller, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);
    }

    private static int GetEntryCount()
    {
        var entries = GetEntriesDictionary();
        return (int)entries.GetType().GetProperty("Count")!.GetValue(entries)!;
    }

    private static void ResetEntries()
    {
        var entries = GetEntriesDictionary();
        entries.GetType().GetMethod("Clear")!.Invoke(entries, null);
    }

    private static void InvalidateFirstEntry()
    {
        var entries = GetEntriesDictionary();
        var keys = (IEnumerable)entries.GetType().GetProperty("Keys")!.GetValue(entries)!;
        var firstKey = keys.Cast<object>().First();
        firstKey.GetType().GetMethod("Invalidate")!.Invoke(firstKey, null);
    }

    private static void AddCurrentTaintsToEntry()
    {
        var entry = CacheDependencyContext.CurrentEntry.Value!;
        var taints = CacheDependencyContext.CurrentTaints.Value!;
        foreach (var taint in taints)
            entry.AddTaint(taint);
    }

    private static object GetEntriesDictionary()
    {
        var entriesField = typeof(CascadingComputeActionFilter<TestController>)
            .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Static)!;

        return entriesField.GetValue(null)!;
    }

    private sealed class StubDependentCacheEntry : IDependentCacheEntry
    {
        public void Invalidate()
        {
        }

        public void AddTaint((string Key, object Value) taint)
        {
        }

        public void OnDependencyAdded(IDependentCacheEntry dependency)
        {
        }
    }

    [CascadingComputeController]
    private sealed partial class TestController : ControllerBase
    {
        public static string CurrentTenant { get; set; } = "tenant-a";

        private readonly TestTenantCacheContextProvider _tenantCacheContextProvider = new();
    }

    private sealed class TestTenantCacheContextProvider : ICacheContextProvider<string>
    {
        public string GetCacheContext() => TestController.CurrentTenant;
    }
}
