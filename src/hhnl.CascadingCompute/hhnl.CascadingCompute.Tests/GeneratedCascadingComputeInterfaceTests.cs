using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class GeneratedCascadingComputeInterfaceTests
{
    [TestMethod]
    public void InterfaceCallUsesGeneratedWrapperCache()
    {
        // Arrange
        var inner = new InterfaceInner();
        inner.CascadingCompute.InvalidateAll();
        IInterfaceInner service = inner;

        // Act
        var first = service.Add(1, 2);
        var second = service.Add(1, 2);

        // Assert
        Assert.AreEqual(3, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void InterfaceInvalidationClearsCacheEntry()
    {
        // Arrange
        var inner = new InterfaceInner();
        inner.CascadingCompute.InvalidateAll();
        IInterfaceInner service = inner;

        // Act
        var first = service.Add(2, 3);
        service.InvalidateAdd(2, 3);
        var second = service.Add(2, 3);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void InterfaceNestedServiceCallsUseGeneratedCaches()
    {
        // Arrange
        var inner = new InterfaceInner();
        var outer = new InterfaceOuter(inner);
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
    public void InterfaceInvalidatingInnerInvalidatesOuter()
    {
        // Arrange
        var inner = new InterfaceInner();
        var outer = new InterfaceOuter(inner);
        inner.CascadingCompute.InvalidateAll();
        outer.CascadingCompute.InvalidateAll();

        // Act
        var first = outer.AddTwice(1, 2);
        inner.InvalidateAdd(1, 2);
        var second = outer.AddTwice(1, 2);

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { (1, 2), (1, 2) }, outer.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (1, 2), (3, 2), (1, 2) }, inner.Calls.ToArray());
    }

    public partial interface IInterfaceInner
    {
        [CascadingCompute]
        int Add(int a, int b);

        void InvalidateAdd(int a, int b);
    }

    public sealed partial class InterfaceInner : IInterfaceInner
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
            => CascadingCompute.InvalidateAdd(a, b);
    }

    public sealed partial class InterfaceOuter
    {
        private readonly IInterfaceInner _inner;
        private readonly List<(int a, int b)> _calls = [];

        public InterfaceOuter(IInterfaceInner inner)
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
