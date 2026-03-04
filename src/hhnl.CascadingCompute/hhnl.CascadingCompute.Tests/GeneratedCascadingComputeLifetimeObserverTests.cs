using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeLifetimeObserverTests
{
    [TestMethod]
    public void Cascading_compute_should_notify_lifetime_observer_for_class_method()
    {
        // Arrange
        ClassLifetimeObserverAttribute.Reset();
        var service = new ObservedService();

        // Act
        _ = service.GetValue(5);
        _ = service.GetValue(5);
        service.InvalidateGetValue(5);

        // Assert
        Assert.AreEqual(1, ClassLifetimeObserverAttribute.CreatedCount);
        Assert.AreEqual(1, ClassLifetimeObserverAttribute.InvalidatedCount);
    }

    [TestMethod]
    public void Cascading_compute_should_notify_lifetime_observer_for_interface_method()
    {
        // Arrange
        InterfaceLifetimeObserverAttribute.Reset();
        IObservedInterface service = new ObservedInterfaceService();

        // Act
        _ = service.GetValue(7);
        _ = service.GetValue(7);
        service.InvalidateGetValue(7);

        // Assert
        Assert.AreEqual(1, InterfaceLifetimeObserverAttribute.CreatedCount);
        Assert.AreEqual(1, InterfaceLifetimeObserverAttribute.InvalidatedCount);
    }

    [TestMethod]
    public void Cascading_compute_should_notify_lifetime_observer_for_field_member()
    {
        // Arrange
        FieldLifetimeObserver.Reset();
        var service = new FieldObservedService();

        // Act
        _ = service.GetValue(11);
        _ = service.GetValue(11);
        service.InvalidateGetValue(11);

        // Assert
        Assert.AreEqual(1, FieldLifetimeObserver.CreatedCount);
        Assert.AreEqual(1, FieldLifetimeObserver.InvalidatedCount);
    }

    [TestMethod]
    public void Cascading_compute_should_notify_lifetime_observer_for_property_member()
    {
        // Arrange
        PropertyLifetimeObserver.Reset();
        var service = new PropertyObservedService();

        // Act
        _ = service.GetValue(13);
        _ = service.GetValue(13);
        service.InvalidateGetValue(13);

        // Assert
        Assert.AreEqual(1, PropertyLifetimeObserver.CreatedCount);
        Assert.AreEqual(1, PropertyLifetimeObserver.InvalidatedCount);
    }

    [TestMethod]
    public void Cascading_compute_should_notify_lifetime_observer_for_primary_constructor_parameter()
    {
        // Arrange
        PrimaryConstructorLifetimeObserver.Reset();
        var service = new PrimaryConstructorObservedService(new PrimaryConstructorLifetimeObserver());

        // Act
        _ = service.GetValue(17);
        _ = service.GetValue(17);
        service.InvalidateGetValue(17);

        // Assert
        Assert.AreEqual(1, PrimaryConstructorLifetimeObserver.CreatedCount);
        Assert.AreEqual(1, PrimaryConstructorLifetimeObserver.InvalidatedCount);
    }

    public sealed partial class ObservedService
    {
        [CascadingCompute]
        [ClassLifetimeObserver]
        public int GetValue(int value)
            => value;

        public void InvalidateGetValue(int value)
            => Invalidation.InvalidateGetValue(value);
    }

    public sealed partial class FieldObservedService
    {
        private readonly FieldLifetimeObserver _observer = new();

        [CascadingCompute]
        public int GetValue(int value)
            => value;

        public void InvalidateGetValue(int value)
            => Invalidation.InvalidateGetValue(value);
    }

    public sealed partial class PropertyObservedService
    {
        private PropertyLifetimeObserver Observer { get; } = new();

        [CascadingCompute]
        public int GetValue(int value)
            => value;

        public void InvalidateGetValue(int value)
            => Invalidation.InvalidateGetValue(value);
    }

    public sealed partial class PrimaryConstructorObservedService(PrimaryConstructorLifetimeObserver observer)
    {
        [CascadingCompute]
        public int GetValue(int value)
            => value;

        public void InvalidateGetValue(int value)
            => Invalidation.InvalidateGetValue(value);
    }

    public partial interface IObservedInterface
    {
        [CascadingCompute]
        [InterfaceLifetimeObserver]
        int GetValue(int value);

        void InvalidateGetValue(int value);
    }

    public sealed partial class ObservedInterfaceService : IObservedInterface
    {
        public int GetValue(int value)
            => value;

        public void InvalidateGetValue(int value)
            => Invalidation.InvalidateGetValue(value);
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ClassLifetimeObserverAttribute : CacheEntryLifetimeObserverAttribute
    {
        public static int CreatedCount { get; private set; }
        public static int InvalidatedCount { get; private set; }

        public static void Reset()
        {
            CreatedCount = 0;
            InvalidatedCount = 0;
        }

        public override void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
            => CreatedCount++;

        public override void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
            => InvalidatedCount++;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InterfaceLifetimeObserverAttribute : CacheEntryLifetimeObserverAttribute
    {
        public static int CreatedCount { get; private set; }
        public static int InvalidatedCount { get; private set; }

        public static void Reset()
        {
            CreatedCount = 0;
            InvalidatedCount = 0;
        }

        public override void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
            => CreatedCount++;

        public override void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
            => InvalidatedCount++;
    }

    public sealed class FieldLifetimeObserver : ICacheEntryLifetimeObserver
    {
        public static int CreatedCount { get; private set; }
        public static int InvalidatedCount { get; private set; }

        public static void Reset()
        {
            CreatedCount = 0;
            InvalidatedCount = 0;
        }

        public void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
            => CreatedCount++;

        public void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
            => InvalidatedCount++;
    }

    public sealed class PropertyLifetimeObserver : ICacheEntryLifetimeObserver
    {
        public static int CreatedCount { get; private set; }
        public static int InvalidatedCount { get; private set; }

        public static void Reset()
        {
            CreatedCount = 0;
            InvalidatedCount = 0;
        }

        public void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
            => CreatedCount++;

        public void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
            => InvalidatedCount++;
    }

    public sealed class PrimaryConstructorLifetimeObserver : ICacheEntryLifetimeObserver
    {
        public static int CreatedCount { get; private set; }
        public static int InvalidatedCount { get; private set; }

        public static void Reset()
        {
            CreatedCount = 0;
            InvalidatedCount = 0;
        }

        public void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
            => CreatedCount++;

        public void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
            => InvalidatedCount++;
    }
}
