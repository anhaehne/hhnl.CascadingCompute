using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;
using hhnl.CascadingCompute.Tests.OtherAssembly.CrossAssembly.Contracts;

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
    public void Cascading_compute_should_include_cache_context_provider_values_for_cross_assembly_interface_implementation()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        ICrossAssemblyService service = new CrossAssemblyContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.Multiply(2, 3);
        _ = service.Multiply(2, 3);
        tenantContextProvider.Context = "tenant-b";
        _ = service.Multiply(2, 3);
        userContextProvider.Context = 8;
        _ = service.Multiply(2, 3);

        // Assert
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3), (2, 3) }, ((CrossAssemblyContextAwareService)service).Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_add_cache_context_provider_taints_to_cache_entries()
    {
        // Arrange
        CacheContextTaintObserverAttribute.Reset();
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        var service = new TaintObservedContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.CascadingCompute.GetValue(10);

        // Assert
        var taints = CacheContextTaintObserverAttribute.LastCreatedTaints;
        Assert.IsNotNull(taints);
        Assert.IsGreaterThanOrEqualTo(taints.Count, 2, "Expected at least two taints from cache-context providers.");

        var tenantKey = taints.Keys.FirstOrDefault(key => key.EndsWith("|string", StringComparison.Ordinal));
        var userKey = taints.Keys.FirstOrDefault(key => key.EndsWith("|int", StringComparison.Ordinal));
        Assert.IsNotNull(tenantKey, $"Tenant taint key not found. Keys: {string.Join(", ", taints.Keys)}");
        Assert.IsNotNull(userKey, $"User taint key not found. Keys: {string.Join(", ", taints.Keys)}");
        Assert.AreEqual("tenant-a", taints[tenantKey!]);
        Assert.AreEqual(7, taints[userKey!]);
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_current_cross_assembly_interface_cache_context_entry()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        ICrossAssemblyService service = new CrossAssemblyContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.Multiply(3, 4);
        tenantContextProvider.Context = "tenant-b";
        _ = service.Multiply(3, 4);
        tenantContextProvider.Context = "tenant-a";
        service.InvalidateMultiply(3, 4);
        _ = service.Multiply(3, 4);
        tenantContextProvider.Context = "tenant-b";
        _ = service.Multiply(3, 4);

        // Assert
        CollectionAssert.AreEqual(new[] { (3, 4), (3, 4), (3, 4) }, ((CrossAssemblyContextAwareService)service).Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_include_primary_constructor_cache_context_for_cross_assembly_interface_implementation()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        ICrossAssemblyService service = new PrimaryConstructorCrossAssemblyContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.Multiply(2, 3);
        _ = service.Multiply(2, 3);
        tenantContextProvider.Context = "tenant-b";
        _ = service.Multiply(2, 3);
        userContextProvider.Context = 8;
        _ = service.Multiply(2, 3);

        // Assert
        CollectionAssert.AreEqual(new[] { (2, 3), (2, 3), (2, 3) }, ((PrimaryConstructorCrossAssemblyContextAwareService)service).Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_entries_matching_cache_context_parameters()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var userContextProvider = new MutableCacheContextProvider<int>(7);
        var service = new ContextAwareService(tenantContextProvider, userContextProvider);

        // Act
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-a";
        _ = service.GetValue(10);
        service.InvalidateGetValue(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_primary_constructor_cache_context_entries_by_parameters()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-a";
        _ = service.GetValue(10);
        service.Invalidate(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_primary_constructor_cache_context_entries_by_predicate()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        service.Invalidate((value, tenant) => value == 10 && tenant == "tenant-a");
        tenantContextProvider.Context = "tenant-a";
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_primary_constructor_constructor_argument_cache_context_entries_by_parameters()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorConstructorArgumentContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-a";
        _ = service.GetValue(10);
        service.Invalidate(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_invalidate_primary_constructor_constructor_argument_cache_context_entries_by_predicate()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorConstructorArgumentContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);
        service.Invalidate((value, tenant) => value == 10 && tenant == "tenant-a");
        tenantContextProvider.Context = "tenant-a";
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_include_primary_constructor_cache_context_provider_without_field_or_property()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_include_primary_constructor_cache_context_provider_written_to_field()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorFieldContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10 }, service.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_include_primary_constructor_cache_context_provider_written_to_constructor_call()
    {
        // Arrange
        var tenantContextProvider = new MutableCacheContextProvider<string>("tenant-a");
        var service = new PrimaryConstructorConstructorArgumentContextAwareService(tenantContextProvider);

        // Act
        _ = service.GetValue(10);
        _ = service.GetValue(10);
        tenantContextProvider.Context = "tenant-b";
        _ = service.GetValue(10);

        // Assert
        CollectionAssert.AreEqual(new[] { 10, 10 }, service.Calls.ToArray());
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
        service.InvalidateGetValue((value, tenantContext, userContext) => value == 10 && tenantContext == "tenant-a" && userContext == 7);
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

        public void InvalidateGetValue(Func<int, string, int, bool> predicate)
            => Invalidation.InvalidateGetValue(predicate);

        public void InvalidateGetValue(int value)
            => Invalidation.InvalidateGetValue(value);
    }

    public sealed partial class TaintObservedContextAwareService
    {
        private readonly MutableCacheContextProvider<string> _tenantContextProvider;

        public TaintObservedContextAwareService(MutableCacheContextProvider<string> tenantContextProvider, MutableCacheContextProvider<int> userContextProvider)
        {
            _tenantContextProvider = tenantContextProvider;
            UserContextProvider = userContextProvider;
        }

        public MutableCacheContextProvider<int> UserContextProvider { get; }

        [CascadingCompute]
        [CacheContextTaintObserver]
        public int GetValue(int value)
            => value;
    }

    public sealed partial class CrossAssemblyContextAwareService : ICrossAssemblyService
    {
        private readonly MutableCacheContextProvider<string> _tenantContextProvider;

        public CrossAssemblyContextAwareService(MutableCacheContextProvider<string> tenantContextProvider, MutableCacheContextProvider<int> userContextProvider)
        {
            _tenantContextProvider = tenantContextProvider;
            UserContextProvider = userContextProvider;
        }

        public MutableCacheContextProvider<int> UserContextProvider { get; }

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

    public sealed partial class PrimaryConstructorCrossAssemblyContextAwareService(MutableCacheContextProvider<string> tenantContextProvider, MutableCacheContextProvider<int> userContextProvider) : ICrossAssemblyService
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

    public sealed partial class PrimaryConstructorFieldContextAwareService(MutableCacheContextProvider<string> tenantContextProvider)
    {
        private readonly MutableCacheContextProvider<string> _tenantContextProvider = tenantContextProvider;
        private readonly List<int> _calls = [];

        public IReadOnlyList<int> Calls => _calls;

        [CascadingCompute]
        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }

        public void Invalidate(int value)
            => Invalidation.InvalidateGetValue(value);

        public void Invalidate(Func<int, string, bool> predicate)
            => Invalidation.InvalidateGetValue(predicate);
    }

    public sealed partial class PrimaryConstructorConstructorArgumentContextAwareService(MutableCacheContextProvider<string> tenantContextProvider)
    {
        private readonly ContextProviderSink _sink = new(tenantContextProvider);
        private readonly List<int> _calls = [];

        public IReadOnlyList<int> Calls => _calls;

        [CascadingCompute]
        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }

        public void Invalidate(int value)
            => Invalidation.InvalidateGetValue(value);

        public void Invalidate(Func<int, string, bool> predicate)
            => Invalidation.InvalidateGetValue(predicate);
    }

    public sealed class ContextProviderSink(MutableCacheContextProvider<string> provider)
    {
        public MutableCacheContextProvider<string> Provider { get; } = provider;
    }

    public sealed class MutableCacheContextProvider<TContext>(TContext context) : ICacheContextProvider<TContext>
    {
        public TContext Context { get; set; } = context;

        public TContext GetCacheContext()
            => Context;
    }

    public sealed partial class PrimaryConstructorContextAwareService(MutableCacheContextProvider<string> tenantContextProvider)
    {
        private readonly List<int> _calls = [];

        public IReadOnlyList<int> Calls => _calls;

        [CascadingCompute]
        public int GetValue(int value)
        {
            _calls.Add(value);
            return value;
        }

        public void Invalidate(int value)
            => Invalidation.InvalidateGetValue(value);

        public void Invalidate(Func<int, string, bool> predicate)
            => Invalidation.InvalidateGetValue(predicate);
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

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CacheContextTaintObserverAttribute : CacheEntryLifetimeObserverAttribute
    {
        public static IReadOnlyDictionary<string, object>? LastCreatedTaints { get; private set; }

        public static void Reset()
            => LastCreatedTaints = null;

        public override void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
        {
            var taintsProperty = cacheEntry.GetType().GetProperty("Taints");
            if (taintsProperty?.GetValue(cacheEntry) is IReadOnlySet<(string Key, object Value)> taints)
                LastCreatedTaints = taints.ToDictionary(taint => taint.Key, taint => taint.Value);
        }

        public override void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
        {
        }
    }
}
