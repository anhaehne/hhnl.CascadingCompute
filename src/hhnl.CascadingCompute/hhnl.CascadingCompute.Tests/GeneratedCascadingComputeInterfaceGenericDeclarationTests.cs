using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

public sealed partial class GeneratedCascadingComputeInterfaceTests
{
    [TestMethod]
    public void Cascading_compute_should_cache_call_result_for_generic_interface_declaration()
    {
        // Arrange
        var service = new GenericInterfaceService<int>();
        IGenericInterfaceService<int> generic = service;

        // Act
        var first = generic.Echo(7);
        var second = generic.Echo(7);

        // Assert
        Assert.AreEqual(7, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { 7 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_recompute_after_cache_invalidation_for_generic_interface_declaration()
    {
        // Arrange
        var service = new GenericInterfaceService<int>();
        IGenericInterfaceService<int> generic = service;

        // Act
        var first = generic.Echo(8);
        generic.InvalidateEcho(8);
        var second = generic.Echo(8);

        // Assert
        Assert.AreEqual(8, first);
        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(new[] { 8, 8 }, service.Calls.ToArray());
    }

    public partial interface IGenericInterfaceService<T>
    {
        [CascadingCompute]
        T Echo(T value);

        void InvalidateEcho(T value);
    }

    public sealed partial class GenericInterfaceService<T> : IGenericInterfaceService<T>
    {
        private readonly List<T> _calls = [];

        public IReadOnlyList<T> Calls => _calls;

        public T Echo(T value)
        {
            _calls.Add(value);
            return value;
        }

        public void InvalidateEcho(T value)
            => CascadingCompute.InvalidateEcho(value);
    }
}
