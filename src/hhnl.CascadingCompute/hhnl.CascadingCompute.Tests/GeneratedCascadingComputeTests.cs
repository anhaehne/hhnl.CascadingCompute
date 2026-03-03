using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeTests
{
    [TestMethod]
    public void Cascading_compute_should_cache_call_result()
    {
        // Arrange
        var service = new InnerService();

        // Act
        var first = service.Add(1, 2);
        var second = service.Add(1, 2);

        // Assert
        Assert.AreEqual(3, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_after_cache_invalidation()
    {
        // Arrange
        var service = new InnerService();

        // Act
        var first = service.Add(2, 3);
        service.InvalidateAdd(2, 3);
        var second = service.Add(2, 3);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3) }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_entries_matching_predicate_invalidation()
    {
        // Arrange
        var service = new InnerService();

        // Act
        _ = service.Add(2, 3);
        _ = service.Add(4, 5);
        _ = service.Add(2, 3);
        _ = service.Add(4, 5);
        service.InvalidateAdd((a, b) => a == 2 && b == 3);
        _ = service.Add(2, 3);
        _ = service.Add(4, 5);

        // Assert
        CollectionAssert.AreEqual(new[] { (2, 3), (4, 5), (2, 3) }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_use_nested_service_caches()
    {
        // Arrange
        var inner = new InnerService();
        var outer = new OuterService(inner);

        // Act
        var first = outer.AddTwice(1, 2);
        var second = outer.AddTwice(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_outer_cache_when_inner_cache_is_invalidated()
    {
        // Arrange
        var inner = new InnerService();
        var outer = new OuterService(inner);

        // Act
        var first = outer.AddTwice(1, 2);
        inner.InvalidateAdd(1, 2);
        var second = outer.AddTwice(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2), (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2), (1, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_mark_classes_with_cascading_methods_as_enabled()
    {
        Assert.IsTrue(typeof(InnerService).IsDefined(typeof(CascadingComputeEnabledAttribute), inherit: false));
        Assert.IsTrue(typeof(OuterService).IsDefined(typeof(CascadingComputeEnabledAttribute), inherit: false));
    }

    public sealed partial class InnerService
    {
        private readonly List<(int a, int b)> _calls = [];

        public IReadOnlyList<(int a, int b)> Calls => _calls;

        [CascadingCompute]
        public int Add(int a, int b)
        {
            _calls.Add((a, b));
            return a + b;
        }

        public void InvalidateAdd(int a, int b)
            => Invalidation.InvalidateAdd(a, b);

        public void InvalidateAdd(Func<int, int, bool> predicate)
            => Invalidation.InvalidateAdd(predicate);

    }

    public sealed partial class OuterService
    {
        private readonly InnerService _inner;
        private readonly List<(int a, int b)> _calls = [];

        public OuterService(InnerService inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<(int a, int b)> Calls => _calls;

        [CascadingCompute]
        public int AddTwice(int a, int b)
        {
            _calls.Add((a, b));
            var first = _inner.Add(a, b);
            return _inner.Add(first, b);
        }
    }

}
