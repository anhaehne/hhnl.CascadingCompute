using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeGenericTests
{
    [TestMethod]
    public void Cascading_compute_should_cache_generic_call_result()
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
    public void Cascading_compute_should_cache_nullable_generic_call_result()
    {
        // Arrange
        var service = new GenericService();

        // Act
        var first = service.CascadingCompute.Echo<string?>(null);
        var second = service.CascadingCompute.Echo<string?>(null);

        // Assert
        Assert.IsNull(first);
        Assert.IsNull(second);
        CollectionAssert.AreEqual(
            new[] { (typeof(string), (object?)null) },
            service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_after_generic_cache_invalidation()
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

    [TestMethod]
    public void Cascading_compute_should_cache_call_with_multiple_generic_arguments()
    {
        // Arrange
        var service = new GenericService();

        // Act
        var first = service.CascadingCompute.Pair<int, string>(1, "value");
        var second = service.CascadingCompute.Pair<int, string>(1, "value");

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[] { (typeof(int), typeof(string), (object)1, (object)"value") },
            service.PairCalls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_generic_call_with_multiple_generic_arguments_after_cache_invalidation()
    {
        // Arrange
        var service = new GenericService();

        // Act
        var first = service.CascadingCompute.Pair<int, string>(2, "x");
        service.CascadingCompute.InvalidatePair<int, string>(2, "x");
        var second = service.CascadingCompute.Pair<int, string>(2, "x");

        // Assert
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(
            new[]
            {
                (typeof(int), typeof(string), (object)2, (object)"x"),
                (typeof(int), typeof(string), (object)2, (object)"x")
            },
            service.PairCalls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_cache_call_result_for_generic_service_declaration()
    {
        // Arrange
        var service = new TypedGenericService<int>();

        // Act
        var first = service.Identity(4);
        var second = service.Identity(4);

        // Assert
        Assert.AreEqual(4, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { 4 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_after_cache_invalidation_for_generic_service_declaration()
    {
        // Arrange
        var service = new TypedGenericService<int>();

        // Act
        var first = service.Identity(5);
        service.InvalidateIdentity(5);
        var second = service.Identity(5);

        // Assert
        Assert.AreEqual(5, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { 5, 5 }, service.Calls.ToArray());
    }


    public sealed partial class GenericService
    {
        private readonly List<(Type type, object? value)> _calls = [];
        private readonly List<(Type leftType, Type rightType, object? leftValue, object? rightValue)> _pairCalls = [];

        public IReadOnlyList<(Type type, object? value)> Calls => _calls;
        public IReadOnlyList<(Type leftType, Type rightType, object? leftValue, object? rightValue)> PairCalls => _pairCalls;

        [CascadingCompute]
        public T Echo<T>(T value)
        {
            _calls.Add((typeof(T), value));
            return value;
        }

        [CascadingCompute]
        public (TLeft Left, TRight Right) Pair<TLeft, TRight>(TLeft left, TRight right)
        {
            _pairCalls.Add((typeof(TLeft), typeof(TRight), left, right));
            return (left, right);
        }
    }

    public sealed partial class TypedGenericService<T>
    {
        private readonly List<T> _calls = [];

        public IReadOnlyList<T> Calls => _calls;

        [CascadingCompute]
        public T Identity(T value)
        {
            _calls.Add(value);
            return value;
        }

        public void InvalidateIdentity(T value)
            => CascadingCompute.InvalidateIdentity(value);
    }
}
