using hhnl.CascadingCompute.Attributes;
using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeIgnoreAttributeTests
{
    [TestMethod]
    public void Ignore_attribute_on_interface_parameter_should_exclude_parameter_from_cache_key_and_invalidate_signature()
    {
        // Arrange
        IInterfaceParameterIgnoreService service = new InterfaceParameterIgnoreService();

        // Act
        _ = service.Combine(2, "A");
        _ = service.Combine(2, "B");
        service.InvalidateCombine(2);
        _ = service.Combine(2, "C");

        // Assert
        CollectionAssert.AreEqual(new[] { (2, "A"), (2, "C") }, ((InterfaceParameterIgnoreService)service).Calls.ToArray());
    }

    [TestMethod]
    public void Ignore_attribute_on_parameter_should_exclude_parameter_from_cache_key_and_invalidate_signature()
    {
        // Arrange
        var service = new ParameterIgnoreService();

        // Act
        _ = service.Combine(1, "A");
        _ = service.Combine(1, "B");
        service.CascadingCompute.InvalidateCombine(1);
        _ = service.Combine(1, "C");

        // Assert
        CollectionAssert.AreEqual(new[] { (1, "A"), (1, "C") }, service.Calls.ToArray());
    }

    public sealed partial class ParameterIgnoreService
    {
        private readonly List<(int id, string label)> _calls = [];

        public IReadOnlyList<(int id, string label)> Calls => _calls;

        [CascadingCompute]
        public int Combine(int id, [CascadingComputeIgnore] string label)
        {
            _calls.Add((id, label));
            return id + label.Length;
        }
    }

    public partial interface IInterfaceParameterIgnoreService
    {
        [CascadingCompute]
        int Combine(int id, [hhnl.CascadingCompute.Attributes.CascadingComputeIgnore] string label);

        void InvalidateCombine(int id);
    }

    public sealed partial class InterfaceParameterIgnoreService : IInterfaceParameterIgnoreService
    {
        private readonly List<(int id, string label)> _calls = [];

        public IReadOnlyList<(int id, string label)> Calls => _calls;

        public int Combine(int id, string label)
        {
            _calls.Add((id, label));
            return id + label.Length;
        }

        public void InvalidateCombine(int id)
            => CascadingCompute.InvalidateCombine(id);
    }

}
