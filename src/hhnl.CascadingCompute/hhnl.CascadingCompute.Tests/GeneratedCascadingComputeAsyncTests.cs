using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeAsyncTests
{
    [TestMethod]
    public async Task Cascading_compute_should_cache_async_call_result()
    {
        // Arrange
        var service = new AsyncInnerService();

        // Act
        var first = await service.AddAsync(1, 2);
        var second = await service.AddAsync(1, 2);

        // Assert
        Assert.AreEqual(3, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, service.Calls.ToArray());
    }

    [TestMethod]
    public async Task Cascading_compute_should_recompute_after_async_cache_invalidation()
    {
        // Arrange
        var service = new AsyncInnerService();

        // Act
        var first = await service.AddAsync(2, 3);
        service.InvalidateAddAsync(2, 3);
        var second = await service.AddAsync(2, 3);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3) }, service.Calls.ToArray());
    }

    [TestMethod]
    public async Task Cascading_compute_should_use_nested_service_caches_for_async_calls()
    {
        // Arrange
        var inner = new AsyncInnerService();
        var outer = new AsyncOuterService(inner);

        // Act
        var first = await outer.AddTwiceAsync(1, 2);
        var second = await outer.AddTwiceAsync(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public async Task Cascading_compute_should_invalidate_outer_cache_when_inner_async_cache_is_invalidated()
    {
        // Arrange
        var inner = new AsyncInnerService();
        var outer = new AsyncOuterService(inner);

        // Act
        var first = await outer.AddTwiceAsync(1, 2);
        inner.InvalidateAddAsync(1, 2);
        var second = await outer.AddTwiceAsync(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2), (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2), (1, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public async Task Cascading_compute_should_intercept_async_optional_cancellation_token_parameter()
    {
        // Arrange
        var service = new AsyncOptionalCancellationTokenService();
        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        var first = await service.GetAsync(5);
        var second = await service.GetAsync(5, cancellationTokenSource.Token);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        Assert.HasCount(1, service.Calls);
        Assert.AreEqual(default, service.Calls[0].token);
    }

    [TestMethod]
    public async Task Cascading_compute_should_cache_async_nullable_reference_call_result()
    {
        // Arrange
        var service = new AsyncNullableReferenceService();

        // Act
        var first = await service.EchoAsync(null);
        var second = await service.EchoAsync(null);

        // Assert
        Assert.IsNull(first);
        Assert.IsNull(second);
        CollectionAssert.AreEqual(new string?[] { null }, service.Calls.ToArray());
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

        public void InvalidateAddAsync(int a, int b)
            => Invalidation.InvalidateAddAsync(a, b);
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

    public sealed partial class AsyncOptionalCancellationTokenService
    {
        private readonly List<(int value, CancellationToken token)> _calls = [];

        public IReadOnlyList<(int value, CancellationToken token)> Calls => _calls;

        [CascadingCompute]
        public async Task<int> GetAsync(int value, CancellationToken cancellationToken = default)
        {
            _calls.Add((value, cancellationToken));
            await Task.Yield();
            return value;
        }
    }

    public sealed partial class AsyncNullableReferenceService
    {
        private readonly List<string?> _calls = [];

        public IReadOnlyList<string?> Calls => _calls;

        [CascadingCompute]
        public async Task<string?> EchoAsync(string? value)
        {
            _calls.Add(value);
            await Task.Yield();
            return value;
        }
    }
}