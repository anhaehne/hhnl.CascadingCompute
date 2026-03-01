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
        service.CascadingCompute.InvalidateGetValue(5);

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

    public sealed partial class ObservedService
    {
        [CascadingCompute]
        [ClassLifetimeObserver]
        public int GetValue(int value)
            => value;
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
            => CascadingCompute.InvalidateGetValue(value);
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
}
