using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class GeneratedCascadingComputeTests
{
    [TestMethod]
    public void InterceptorUsesGeneratedWrapperCache()
    {
        // Arrange
        var service = new InnerService();
        service.CascadingCompute.InvalidateAll();

        // Act
        var first = service.Add(1, 2);
        var second = service.Add(1, 2);

        // Assert
        Assert.AreEqual(3, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, service.Calls.ToArray());
    }

    [TestMethod]
    public void InvalidateClearsGeneratedCacheEntry()
    {
        // Arrange
        var service = new InnerService();
        service.CascadingCompute.InvalidateAll();

        // Act
        var first = service.Add(2, 3);
        service.CascadingCompute.InvalidateAdd(2, 3);
        var second = service.Add(2, 3);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3) }, service.Calls.ToArray());
    }

    [TestMethod]
    public void NestedServiceCallsUseGeneratedCaches()
    {
        // Arrange
        var inner = new InnerService();
        var outer = new OuterService(inner);
        inner.CascadingCompute.InvalidateAll();
        outer.CascadingCompute.InvalidateAll();

        // Act
        var first = outer.AddTwice(1, 2);
        var second = outer.AddTwice(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void InvalidateInnerServiceInvalidatesOuterService()
    {
        // Arrange
        var inner = new InnerService();
        var outer = new OuterService(inner);
        inner.CascadingCompute.InvalidateAll();
        outer.CascadingCompute.InvalidateAll();

        // Act
        var first = outer.AddTwice(1, 2);
        inner.CascadingCompute.InvalidateAdd(1, 2);
        var second = outer.AddTwice(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2), (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2), (1, 2) }, inner.Calls.ToArray());
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
