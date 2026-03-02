using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeInterceptorIgnoredParameterTypesTests
{
    [TestMethod]
    public void Cascading_compute_should_ignore_configured_parameter_types_for_interface_methods()
    {
        // Arrange
        IInterfaceCancellationTokenService service = new InterfaceCancellationTokenService();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        // Act
        _ = service.Get(7, cts1.Token);
        _ = service.Get(7, cts2.Token);
        service.InvalidateGet(7);
        _ = service.Get(7, cts2.Token);

        // Assert
        CollectionAssert.AreEqual(new[] { (7, cts1.Token), (7, cts2.Token) }, ((InterfaceCancellationTokenService)service).Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_ignore_configured_parameter_types_in_cache_key_and_invalidation()
    {
        // Arrange
        var service = new CancellationTokenService();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        // Act
        _ = service.Get(5, cts1.Token);
        _ = service.Get(5, cts2.Token);
        service.InvalidateGet(5);
        _ = service.Get(5, cts2.Token);

        // Assert
        CollectionAssert.AreEqual(new[] { (5, cts1.Token), (5, cts2.Token) }, service.Calls.ToArray());
    }

    public sealed partial class CancellationTokenService
    {
        private readonly List<(int value, CancellationToken token)> _calls = [];

        public IReadOnlyList<(int value, CancellationToken token)> Calls => _calls;

        [CascadingCompute]
        public int Get(int value, CancellationToken cancellationToken)
        {
            _calls.Add((value, cancellationToken));
            return value;
        }

        public void InvalidateGet(int value)
            => Invalidation.InvalidateGet(value);
    }

    public partial interface IInterfaceCancellationTokenService
    {
        [CascadingCompute]
        int Get(int value, CancellationToken cancellationToken);

        void InvalidateGet(int value);
    }

    public sealed partial class InterfaceCancellationTokenService : IInterfaceCancellationTokenService
    {
        private readonly List<(int value, CancellationToken token)> _calls = [];

        public IReadOnlyList<(int value, CancellationToken token)> Calls => _calls;

        public int Get(int value, CancellationToken cancellationToken)
        {
            _calls.Add((value, cancellationToken));
            return value;
        }

        public void InvalidateGet(int value)
            => Invalidation.InvalidateGet(value);
    }
}
