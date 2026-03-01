using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeTests
{
    [TestMethod]
    public void InterceptorUsesGeneratedWrapperCache()
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
    public void InvalidateClearsGeneratedCacheEntry()
    {
        // Arrange
        var service = new InnerService();

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

        // Act
        var first = outer.AddTwice(1, 2);
        inner.CascadingCompute.InvalidateAdd(1, 2);
        var second = outer.AddTwice(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2), (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2), (1, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void GenericMethodUsesGeneratedWrapperCache()
    {
        // Arrange
        var service = new GenericService();

        // Act
        var first = service.CascadingCompute.Echo(1);
        var second = service.CascadingCompute.Echo(1);
        // Assert
        Assert.AreEqual(1, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), (object)1) },
            service.Calls.ToArray());
    }

    [TestMethod]
    public void GenericMethodInvalidationClearsCacheEntry()
    {
        // Arrange
        var service = new GenericService();

        // Act
        var first = service.CascadingCompute.Echo(2);
        service.CascadingCompute.InvalidateEcho<int>(2);
        var second = service.CascadingCompute.Echo(2);

        // Assert
        Assert.AreEqual(2, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), (object)2), (typeof(int), (object)2) },
            service.Calls.ToArray());
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

    public sealed partial class GenericService
    {
        private readonly List<(Type type, object value)> _calls = [];

        public IReadOnlyList<(Type type, object value)> Calls => _calls;

        [CascadingCompute]
        public T Echo<T>(T value)
        {
            _calls.Add((typeof(T), value!));
            return value;
        }
    }
}
