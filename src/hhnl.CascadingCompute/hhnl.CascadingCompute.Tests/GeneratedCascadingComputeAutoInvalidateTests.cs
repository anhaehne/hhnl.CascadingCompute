using hhnl.CascadingCompute.Attributes;
using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeAutoInvalidateTests
{
    [TestMethod]
    public async Task Cascading_compute_should_auto_invalidate_class_method_cache_entry()
    {
        // Arrange
        var service = new AutoInvalidatedService();

        // Act
        _ = service.GetValue(5);
        _ = service.GetValue(5);
        await Task.Delay(50);
        _ = service.GetValue(5);

        // Assert
        CollectionAssert.AreEqual(new[] { 5, 5 }, service.Calls.ToArray());
    }

    [TestMethod]
    public async Task Cascading_compute_should_auto_invalidate_interface_method_cache_entry()
    {
        // Arrange
        IAutoInvalidatedInterfaceService service = new AutoInvalidatedInterfaceService();

        // Act
        _ = service.GetValue(7);
        _ = service.GetValue(7);
        await Task.Delay(50);
        _ = service.GetValue(7);

        // Assert
        CollectionAssert.AreEqual(new[] { 7, 7 }, ((AutoInvalidatedInterfaceService)service).Calls.ToArray());
    }

    public sealed partial class AutoInvalidatedService
    {
        private readonly List<int> _calls = [];

        public IReadOnlyList<int> Calls => _calls;

        [CascadingCompute]
        [AutoInvalidate(25)]
        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }
    }

    public partial interface IAutoInvalidatedInterfaceService
    {
        [CascadingCompute]
        [AutoInvalidate(25)]
        int GetValue(int value);
    }

    public sealed partial class AutoInvalidatedInterfaceService : IAutoInvalidatedInterfaceService
    {
        private readonly List<int> _calls = [];

        public IReadOnlyList<int> Calls => _calls;

        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }
    }
}
