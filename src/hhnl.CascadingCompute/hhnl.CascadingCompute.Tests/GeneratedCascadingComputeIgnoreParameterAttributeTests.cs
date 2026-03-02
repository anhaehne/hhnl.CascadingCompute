using hhnl.CascadingCompute.Attributes;
using hhnl.CascadingCompute.Shared.Attributes;

[assembly: CascadingComputeIgnoreParameter(typeof(hhnl.CascadingCompute.Tests.AssemblyIgnoredMarker))]

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeIgnoreParameterAttributeTests
{
    [TestMethod]
    public void Ignore_parameter_attribute_on_assembly_should_exclude_matching_type_from_cache_key_and_invalidate_signature()
    {
        // Arrange
        var service = new AssemblyLevelIgnoreParameterService();

        // Act
        _ = service.Compose(11, new AssemblyIgnoredMarker("north"));
        _ = service.Compose(11, new AssemblyIgnoredMarker("south"));
        service.CascadingCompute.InvalidateCompose(11);
        _ = service.Compose(11, new AssemblyIgnoredMarker("west"));

        // Assert
        CollectionAssert.AreEqual(new[] { (11, "north"), (11, "west") }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Ignore_parameter_attribute_on_method_should_exclude_named_parameter_from_cache_key_and_invalidate_signature()
    {
        // Arrange
        var service = new MethodLevelIgnoreParameterService();

        // Act
        _ = service.Combine(1, "A");
        _ = service.Combine(1, "B");
        service.CascadingCompute.InvalidateCombine(1);
        _ = service.Combine(1, "C");

        // Assert
        CollectionAssert.AreEqual(new[] { (1, "A"), (1, "C") }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Ignore_parameter_attribute_should_exclude_ignored_parameter_from_predicate_invalidate_signature()
    {
        // Arrange
        var service = new MethodLevelIgnoreParameterService();

        // Act
        _ = service.Combine(1, "A");
        _ = service.Combine(1, "B");
        _ = service.Combine(2, "X");
        service.CascadingCompute.InvalidateCombine(id => id == 1);
        _ = service.Combine(1, "C");
        _ = service.Combine(2, "Y");

        // Assert
        CollectionAssert.AreEqual(new[] { (1, "A"), (2, "X"), (1, "C") }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Ignore_parameter_attribute_on_type_should_exclude_matching_type_from_cache_key_and_invalidate_signature()
    {
        // Arrange
        var service = new TypeLevelIgnoreParameterService();

        // Act
        _ = service.Compose("north", 7);
        _ = service.Compose("south", 7);
        service.CascadingCompute.InvalidateCompose(7);
        _ = service.Compose("west", 7);

        // Assert
        CollectionAssert.AreEqual(new[] { ("north", 7), ("west", 7) }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Ignore_parameter_attribute_on_interface_should_exclude_named_parameter_from_cache_key_and_invalidate_signature()
    {
        // Arrange
        IInterfaceLevelIgnoreParameterService service = new InterfaceLevelIgnoreParameterService();

        // Act
        _ = service.Compute(5, "A");
        _ = service.Compute(5, "B");
        service.InvalidateCompute(5);
        _ = service.Compute(5, "C");

        // Assert
        CollectionAssert.AreEqual(new[] { (5, "A"), (5, "C") }, ((InterfaceLevelIgnoreParameterService)service).Calls.ToArray());
    }

    public sealed partial class MethodLevelIgnoreParameterService
    {
        private readonly List<(int id, string label)> _calls = [];

        public IReadOnlyList<(int id, string label)> Calls => _calls;

        [CascadingCompute]
        [CascadingComputeIgnoreParameter("label")]
        public int Combine(int id, string label)
        {
            _calls.Add((id, label));
            return id + label.Length;
        }
    }

    public sealed partial class AssemblyLevelIgnoreParameterService
    {
        private readonly List<(int id, string marker)> _calls = [];

        public IReadOnlyList<(int id, string marker)> Calls => _calls;

        [CascadingCompute]
        public int Compose(int id, AssemblyIgnoredMarker marker)
        {
            _calls.Add((id, marker.Value));
            return id + marker.Value.Length;
        }
    }

    [CascadingComputeIgnoreParameter(typeof(string))]
    public sealed partial class TypeLevelIgnoreParameterService
    {
        private readonly List<(string region, int id)> _calls = [];

        public IReadOnlyList<(string region, int id)> Calls => _calls;

        [CascadingCompute]
        public int Compose(string region, int id)
        {
            _calls.Add((region, id));
            return id + region.Length;
        }
    }

    [CascadingComputeIgnoreParameter("label")]
    public partial interface IInterfaceLevelIgnoreParameterService
    {
        [CascadingCompute]
        int Compute(int id, string label);

        void InvalidateCompute(int id);
    }

    public sealed partial class InterfaceLevelIgnoreParameterService : IInterfaceLevelIgnoreParameterService
    {
        private readonly List<(int id, string label)> _calls = [];

        public IReadOnlyList<(int id, string label)> Calls => _calls;

        public int Compute(int id, string label)
        {
            _calls.Add((id, label));
            return id + label.Length;
        }

        public void InvalidateCompute(int id)
            => CascadingCompute.InvalidateCompute(id);
    }
}

public sealed class AssemblyIgnoredMarker
{
    public AssemblyIgnoredMarker(string value)
    {
        Value = value;
    }

    public string Value { get; }
}