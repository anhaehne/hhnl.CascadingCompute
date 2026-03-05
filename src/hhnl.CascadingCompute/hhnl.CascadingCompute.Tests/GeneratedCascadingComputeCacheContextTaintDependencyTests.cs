using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public sealed partial class GeneratedCascadingComputeCacheContextTaintDependencyTests
{
    [TestMethod]
    public void Cascading_compute_should_recalculate_nested_services_when_user_context_changes_and_after_invalidation()
    {
        // Arrange
        var userContext = new UserContext { UserId = 0 };
        var inner = new ServiceInner(userContext);
        var middle = new ServiceMiddle(inner);
        var outer = new ServiceOuter(middle, userContext);

        // Act 1
        _ = outer.AddTwice(1, 2);
        _ = outer.AddTwice(1, 2);

        // Assert 1
        CollectionAssert.AreEqual(new long[] { 0 }, outer.Calls.ToArray());
        Assert.AreEqual(1, middle.CallCount);
        CollectionAssert.AreEqual(new long[] { 0 }, inner.Calls.ToArray());

        // Act 2
        userContext.UserId = 1;
        _ = outer.AddTwice(1, 2);

        // Assert 2
        CollectionAssert.AreEqual(new long[] { 0, 1 }, outer.Calls.ToArray());
        Assert.AreEqual(2, middle.CallCount);
        CollectionAssert.AreEqual(new long[] { 0, 1 }, inner.Calls.ToArray());

        // Act 3
        userContext.UserId = 0;
        inner.InvalidateAdd(1, 2);
        _ = outer.AddTwice(1, 2);

        // Assert 3
        CollectionAssert.AreEqual(new long[] { 0, 1, 0 }, outer.Calls.ToArray());
        Assert.AreEqual(3, middle.CallCount);
        CollectionAssert.AreEqual(new long[] { 0, 1, 0 }, inner.Calls.ToArray());
    }

    [TestMethod]
    public void Cascading_compute_should_only_invalidate_entries_for_current_user_context_with_shared_middle_cache()
    {
        // Arrange
        var userContext = new UserContext { UserId = 0 };
        var inner = new ServiceInner(userContext);
        var middle = new ServiceMiddle(inner);
        var outer = new ServiceOuter(middle, userContext);

        // Act 1
        _ = outer.AddTwice(2, 3); // user 0

        userContext.UserId = 1;
        _ = outer.AddTwice(2, 3); // user 1

        // Assert 1
        CollectionAssert.AreEqual(new long[] { 0, 1 }, outer.Calls.ToArray());
        Assert.AreEqual(2, middle.CallCount);
        CollectionAssert.AreEqual(new long[] { 0, 1 }, inner.Calls.ToArray());

        // Act 2
        userContext.UserId = 0;
        inner.InvalidateAdd(2, 3); // invalidates user 0 branch

        // Act 3
        userContext.UserId = 1;
        _ = outer.AddTwice(2, 3); // should stay cached for user 1

        // Assert 2
        CollectionAssert.AreEqual(new long[] { 0, 1 }, outer.Calls.ToArray());
        Assert.AreEqual(2, middle.CallCount);
        CollectionAssert.AreEqual(new long[] { 0, 1 }, inner.Calls.ToArray());
    }

    public sealed class UserContext : ICacheContextProvider<long>
    {
        public long UserId { get; set; }

        public (string Key, long Context) GetCacheContext() => ("user", UserId);
    }

    public partial interface IServiceInner
    {
        [CascadingCompute]
        int Add(int a, int b);
    }

    public sealed partial class ServiceInner(UserContext userContext) : IServiceInner
    {
        private readonly List<long> _calls = [];

        public IReadOnlyList<long> Calls => _calls;

        [CascadingCompute]
        public int Add(int a, int b)
        {
            _calls.Add(userContext.UserId);
            return a + b;
        }

        public void InvalidateAdd(int a, int b)
            => Invalidation.InvalidateAdd(a, b);
    }

    public partial interface IServiceMiddle
    {
        [CascadingCompute]
        int Add(int a, int b);
    }

    public sealed partial class ServiceMiddle(IServiceInner inner) : IServiceMiddle
    {
        public int CallCount { get; private set; }

        [CascadingCompute]
        public int Add(int a, int b)
        {
            CallCount++;
            return inner.Add(a, b);
        }
    }

    public sealed partial class ServiceOuter(IServiceMiddle middle, UserContext userContext)
    {
        private readonly List<long> _calls = [];

        public IReadOnlyList<long> Calls => _calls;

        [CascadingCompute]
        public int AddTwice(int a, int b)
        {
            _calls.Add(userContext.UserId);
            return middle.Add(a, b);
        }
    }
}
