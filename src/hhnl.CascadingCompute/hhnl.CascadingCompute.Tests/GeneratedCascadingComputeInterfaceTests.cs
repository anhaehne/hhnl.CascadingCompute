using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeInterfaceTests
{
    [TestMethod]
    public void Cascading_compute_should_cache_interface_call_result()
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
    public void Cascading_compute_should_recompute_interface_call_after_cache_invalidation()
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
    public void Cascading_compute_should_recompute_interface_entries_matching_predicate_invalidation()
    {
        // Arrange
        var inner = new InterfaceInner();
        IInterfaceInner service = inner;

        // Act
        _ = service.Add(2, 3);
        _ = service.Add(4, 5);
        _ = service.Add(2, 3);
        _ = service.Add(4, 5);
        service.InvalidateAdd((a, b) => a == 2 && b == 3);
        _ = service.Add(2, 3);
        _ = service.Add(4, 5);

        // Assert
        CollectionAssert.AreEqual(new[] { (2, 3), (4, 5), (2, 3) }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_use_nested_interface_service_caches()
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
    public void Cascading_compute_should_invalidate_outer_interface_cache_when_inner_cache_is_invalidated()
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
    public void Cascading_compute_should_allow_interface_and_implementation_cascading_methods_to_be_called()
    {
        // Arrange
        var service = new InterfaceAndImplementationService();
        IInterfaceAndImplementationService viaInterface = service;

        // Act
        var interfaceResult = viaInterface.GetFromInterface(10);
        var implementationResult = service.GetFromImplementation(20);

        // Assert
        Assert.AreEqual(10, interfaceResult);
        Assert.AreEqual(20, implementationResult);
        Assert.AreEqual(1, service.InterfaceCallCount);
        Assert.AreEqual(1, service.ImplementationCallCount);
    }

    [TestMethod]
    public void Cascading_compute_should_cache_generic_interface_call_result()
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
    public void Cascading_compute_should_recompute_generic_interface_call_after_cache_invalidation()
    {
        // Arrange
        var service = new InterfaceGenericService();
        IInterfaceGenericService generic = service;

        // Act
        var first = generic.CascadingCompute.Echo(2);
        generic.InvalidateEcho<int>(2);
        var second = generic.CascadingCompute.Echo(2);

        // Assert
        Assert.AreEqual(2, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), (object)2), (typeof(int), (object)2) },
            service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_cache_generic_interface_call_with_multiple_generic_arguments()
    {
        // Arrange
        var service = new InterfaceGenericService();
        IInterfaceGenericService generic = service;

        // Act
        var first = generic.CascadingCompute.Pair<int, string>(3, "value");
        var second = generic.CascadingCompute.Pair<int, string>(3, "value");

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), typeof(string), (object)3, (object)"value") },
            service.PairCalls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_generic_interface_call_with_multiple_generic_arguments_after_cache_invalidation()
    {
        // Arrange
        var service = new InterfaceGenericService();
        IInterfaceGenericService generic = service;

        // Act
        var first = generic.CascadingCompute.Pair<int, string>(4, "x");
        generic.InvalidatePair<int, string>(4, "x");
        var second = generic.CascadingCompute.Pair<int, string>(4, "x");

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[]
            {
                (typeof(int), typeof(string), (object)4, (object)"x"),
                (typeof(int), typeof(string), (object)4, (object)"x")
            },
            service.PairCalls.ToArray());
    }

    public partial interface IInterfaceInner
    {
        [CascadingCompute]
        int Add(int a, int b);

        void InvalidateAdd(int a, int b);

        void InvalidateAdd(Func<int, int, bool> predicate);

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
            => Invalidation.InvalidateAdd(a, b);

        public void InvalidateAdd(Func<int, int, bool> predicate)
            => Invalidation.InvalidateAdd(predicate);

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

        [CascadingCompute]
        (TLeft Left, TRight Right) Pair<TLeft, TRight>(TLeft left, TRight right);

        void InvalidateEcho<T>(T value);

        void InvalidatePair<TLeft, TRight>(TLeft left, TRight right);
    }

    public sealed partial class InterfaceGenericService : IInterfaceGenericService
    {
        private readonly List<(Type type, object value)> _calls = [];
        private readonly List<(Type leftType, Type rightType, object leftValue, object rightValue)> _pairCalls = [];

        public IReadOnlyList<(Type type, object value)> Calls => _calls;
        public IReadOnlyList<(Type leftType, Type rightType, object leftValue, object rightValue)> PairCalls => _pairCalls;

        [CascadingCompute]
        public T Echo<T>(T value)
        {
            _calls.Add((typeof(T), value!));
            return value;
        }

        [CascadingCompute]
        public (TLeft Left, TRight Right) Pair<TLeft, TRight>(TLeft left, TRight right)
        {
            _pairCalls.Add((typeof(TLeft), typeof(TRight), left!, right!));
            return (left, right);
        }

        public void InvalidateEcho<T>(T value)
            => Invalidation.InvalidateEcho<T>(value);

        public void InvalidatePair<TLeft, TRight>(TLeft left, TRight right)
            => Invalidation.InvalidatePair<TLeft, TRight>(left, right);
    }

    public partial interface IInterfaceAndImplementationService
    {
        [CascadingCompute]
        int GetFromInterface(int value);
    }

    public sealed partial class InterfaceAndImplementationService : IInterfaceAndImplementationService
    {
        public int InterfaceCallCount { get; private set; }
        public int ImplementationCallCount { get; private set; }

        public int GetFromInterface(int value)
        {
            InterfaceCallCount++;
            return value;
        }

        [CascadingCompute]
        public int GetFromImplementation(int value)
        {
            ImplementationCallCount++;
            return value;
        }
    }

}
