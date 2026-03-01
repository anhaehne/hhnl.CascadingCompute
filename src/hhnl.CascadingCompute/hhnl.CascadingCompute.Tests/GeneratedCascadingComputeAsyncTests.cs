using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class GeneratedCascadingComputeAsyncTests
{
    [TestMethod]
    public async Task InterceptorUsesGeneratedWrapperCacheForAsync()
    {
        // Arrange
        var service = new AsyncInnerService();
        service.CascadingCompute.InvalidateAll();

        // Act
        var first = await service.AddAsync(1, 2);
        var second = await service.AddAsync(1, 2);

        // Assert
        Assert.AreEqual(3, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, service.Calls.ToArray());
    }

    [TestMethod]
    public async Task InvalidateClearsGeneratedCacheEntryForAsync()
    {
        // Arrange
        var service = new AsyncInnerService();
        service.CascadingCompute.InvalidateAll();

        // Act
        var first = await service.AddAsync(2, 3);
        service.CascadingCompute.InvalidateAddAsync(2, 3);
        var second = await service.AddAsync(2, 3);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3) }, service.Calls.ToArray());
    }

    [TestMethod]
    public async Task NestedServiceCallsUseGeneratedCachesForAsync()
    {
        // Arrange
        var inner = new AsyncInnerService();
        var outer = new AsyncOuterService(inner);
        inner.CascadingCompute.InvalidateAll();
        outer.CascadingCompute.InvalidateAll();

        // Act
        var first = await outer.AddTwiceAsync(1, 2);
        var second = await outer.AddTwiceAsync(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public async Task InvalidateInnerServiceInvalidatesOuterServiceForAsync()
    {
        // Arrange
        var inner = new AsyncInnerService();
        var outer = new AsyncOuterService(inner);
        inner.CascadingCompute.InvalidateAll();
        outer.CascadingCompute.InvalidateAll();

        // Act
        var first = await outer.AddTwiceAsync(1, 2);
        inner.CascadingCompute.InvalidateAddAsync(1, 2);
        var second = await outer.AddTwiceAsync(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2), (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2), (1, 2) }, inner.Calls.ToArray());
    }

    public sealed partial class AsyncInnerService
    {
        private readonly List<(int a, int b)> _calls = [];

        public IReadOnlyList<(int a, int b)> Calls => _calls;

        [CascadingCompute]
        public async Task<int> AddAsync(int a, int b)
        {
            _calls.Add((a, b));
            await Task.Yield();
            return a + b;
        }
    }

    public sealed partial class AsyncOuterService
    {
        private readonly AsyncInnerService _inner;
        private readonly List<(int a, int b)> _calls = [];

        public AsyncOuterService(AsyncInnerService inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<(int a, int b)> Calls => _calls;

        [CascadingCompute]
        public async Task<int> AddTwiceAsync(int a, int b)
        {
            _calls.Add((a, b));
            var first = await _inner.AddAsync(a, b);
            return await _inner.AddAsync(first, b);
        }
    }
}