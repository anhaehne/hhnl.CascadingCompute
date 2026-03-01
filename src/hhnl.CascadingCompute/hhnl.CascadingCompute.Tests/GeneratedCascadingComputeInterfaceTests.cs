using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeInterfaceTests
{
    [TestMethod]
    public void InterfaceCallUsesGeneratedWrapperCache()
    {
        // Arrange
        var inner = new InterfaceInner();
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
    public void InterfaceGenericMethodUsesGeneratedWrapperCache()
    {
        // Arrange
        var service = new InterfaceGenericService();
        IInterfaceGenericService generic = service;

        // Act
        var first = generic.CascadingCompute.Echo(1);
        var second = generic.CascadingCompute.Echo(1);
        // Assert
        Assert.AreEqual(1, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), (object)1) },
            service.Calls.ToArray());
    }

    [TestMethod]
    public void InterfaceGenericInvalidationClearsCacheEntry()
    {
        // Arrange
        var service = new InterfaceGenericService();
        IInterfaceGenericService generic = service;

        // Act
        var first = generic.CascadingCompute.Echo(2);
        generic.CascadingCompute.InvalidateEcho<int>(2);
        var second = generic.CascadingCompute.Echo(2);

        // Assert
        Assert.AreEqual(2, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), (object)2), (typeof(int), (object)2) },
            service.Calls.ToArray());
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

    public partial interface IInterfaceGenericService
    {
        [CascadingCompute]
        T Echo<T>(T value);

        void InvalidateEcho<T>(T value);
    }

    public sealed partial class InterfaceGenericService : IInterfaceGenericService
    {
        private readonly List<(Type type, object value)> _calls = [];

        public IReadOnlyList<(Type type, object value)> Calls => _calls;

        [CascadingCompute]
        public T Echo<T>(T value)
        {
            _calls.Add((typeof(T), value!));
            return value;
        }

        public void InvalidateEcho<T>(T value)
            => CascadingCompute.InvalidateEcho<T>(value);
    }
}
