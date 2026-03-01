using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeCacheContextProviderTests
{
    [TestMethod]
    public void Cascading_compute_should_include_field_and_property_cache_context_in_cache_key()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        var service = new ContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.GetValue(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        userContextProvider.Context = 9;
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_include_cache_context_provider_values_for_interface_implementation()
    {
        // Arrange
        var tenantContextProvider = new MutableInterfaceCacheContextProvider<string>("tenant-a");
        var regionContextProvider = new MutableInterfaceCacheContextProvider<int>(1);
        IContextAwareInterface service = new ContextAwareInterfaceService(tenantContextProvider, regionContextProvider);

        // Act
        _ = service.GetValue(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        regionContextProvider.Context = 2;
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, ((ContextAwareInterfaceService)service).Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_entries_matching_cache_context_predicate()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        var service = new ContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        service.CascadingCompute.InvalidateGetValue((value, tenantContext, userContext) => value == 10 && tenantContext == "tenant-a" && userContext == 7);
        tenantContextProvider.Context = "tenant-a";
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    public sealed partial class ContextAwareService
    {
        private readonly MutableCacheContextProvider<string> _tenantContextProvider;

        public ContextAwareService(MutableCacheContextProvider<string> tenantContextProvider, MutableCacheContextProvider<int> userContextProvider)
        {
            _tenantContextProvider = tenantContextProvider;
            UserContextProvider = userContextProvider;
        }

        public MutableCacheContextProvider<int> UserContextProvider { get; }

        private readonly List<int> _calls = [];

        public IReadOnlyList<int> Calls => _calls;

        [CascadingCompute]
        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }
    }

    public sealed class MutableCacheContextProvider<TContext>(TContext context) : ICacheContextProvider<TContext>
    {
        public TContext Context { get; set; } = context;

        public TContext GetCacheContext()
            => Context;
    }

    public partial interface IContextAwareInterface
    {
        [CascadingCompute]
        int GetValue(int value);
    }

    public sealed partial class ContextAwareInterfaceService : IContextAwareInterface
    {
        private readonly MutableInterfaceCacheContextProvider<string> _tenantContextProvider;
        public MutableInterfaceCacheContextProvider<int> RegionContextProvider { get; }
        private readonly List<int> _calls = [];

        public ContextAwareInterfaceService(MutableInterfaceCacheContextProvider<string> tenantContextProvider, MutableInterfaceCacheContextProvider<int> regionContextProvider)
        {
            _tenantContextProvider = tenantContextProvider;
            RegionContextProvider = regionContextProvider;
        }

        public IReadOnlyList<int> Calls => _calls;

        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }
    }

    public sealed class MutableInterfaceCacheContextProvider<TContext>(TContext context) : ICacheContextProvider<TContext>
    {
        public TContext Context { get; set; } = context;

        public TContext GetCacheContext()
            => Context;
    }
}
