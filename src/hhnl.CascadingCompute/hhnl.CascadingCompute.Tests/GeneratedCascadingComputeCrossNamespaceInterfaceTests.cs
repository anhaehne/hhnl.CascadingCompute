using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Tests.CrossNamespace.Contracts;
using hhnl.CascadingCompute.Tests.CrossNamespace.Implementations;
using hhnl.CascadingCompute.Tests.OtherAssembly.CrossAssembly.Contracts;

namespace hhnl.CascadingCompute.Tests
{
    [TestClass]
    public sealed class GeneratedCascadingComputeCrossNamespaceInterfaceTests
    {
        [TestMethod]
        public void Cascading_compute_should_cache_without_fully_qualified_cross_namespace_type_names()
        {
            // Arrange
            var service = new CrossNamespaceService();
            ICrossNamespaceService contract = service;

            // Act
            var first = contract.Add(5, 7);
            var second = contract.Add(5, 7);

            // Assert
            Assert.AreEqual(12, first);
            Assert.AreEqual(first, second);
            CollectionAssert.AreEqual(new[] { (5, 7) }, service.Calls.ToArray());
        }

        [TestMethod]
        public void Cascading_compute_should_cache_when_interface_and_implementation_are_in_different_namespaces()
        {
            // Arrange
            var service = new CrossNamespace.Implementations.CrossNamespaceService();
            CrossNamespace.Contracts.ICrossNamespaceService contract = service;

            // Act
            var first = contract.Add(1, 2);
            var second = contract.Add(1, 2);

            // Assert
            Assert.AreEqual(3, first);
            Assert.AreEqual(first, second);
            CollectionAssert.AreEqual(new[] { (1, 2) }, service.Calls.ToArray());
        }

        [TestMethod]
        public void Cascading_compute_should_invalidate_when_interface_and_implementation_are_in_different_namespaces()
        {
            // Arrange
            var service = new CrossNamespace.Implementations.CrossNamespaceService();
            CrossNamespace.Contracts.ICrossNamespaceService contract = service;

            // Act
            _ = contract.Add(2, 3);
            contract.InvalidateAdd(2, 3);
            _ = contract.Add(2, 3);

            // Assert
            CollectionAssert.AreEqual(new[] { (2, 3), (2, 3) }, service.Calls.ToArray());
        }

        [TestMethod]
        public void Cascading_compute_should_cache_when_interface_is_in_different_assembly()
        {
            // Arrange
            var service = new CrossAssemblyService();
            ICrossAssemblyService contract = service;

            // Act
            var first = contract.Multiply(3, 4);
            var second = contract.Multiply(3, 4);

            // Assert
            Assert.AreEqual(12, first);
            Assert.AreEqual(first, second);
            CollectionAssert.AreEqual(new[] { (3, 4) }, service.Calls.ToArray());
        }
    }
}

public sealed partial class CrossAssemblyService : ICrossAssemblyService
{
    private readonly List<(int left, int right)> _calls = [];

    public IReadOnlyList<(int left, int right)> Calls => _calls;

    public int Multiply(int left, int right)
    {
        _calls.Add((left, right));
        return left * right;
    }

    public void InvalidateMultiply(int left, int right)
        => Invalidation.InvalidateMultiply(left, right);
}


namespace hhnl.CascadingCompute.Tests.CrossNamespace.Contracts
{
    public partial interface ICrossNamespaceService
    {
        [CascadingCompute]
        int Add(int a, int b);

        void InvalidateAdd(int a, int b);
    }
}

namespace hhnl.CascadingCompute.Tests.CrossNamespace.Implementations
{
    public sealed partial class CrossNamespaceService : Contracts.ICrossNamespaceService
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
    }
}
