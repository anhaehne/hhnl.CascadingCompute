using System.Runtime.CompilerServices;

namespace hhnl.CascadingCompute.Caching;

public static class CascadingComputeUtils
{
    public static async IAsyncEnumerable<TResult> ExecuteCascadingComputeAsAsyncEnumerable<TResult>(
        Func<CancellationToken, Task<TResult>> func,
        bool throwWithoutDepdendency = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var invalidation = new AsyncInvalidation(cancellationToken);
            CacheDependencyContext.CurrentEntry.Value = invalidation;

            var next = await func(cancellationToken);

            if (!invalidation.HasDependencies && throwWithoutDepdendency)
                throw new InvalidOperationException("Cascading compute function did not register any dependencies. This method will never return new data. If this is intentional, set throwWithoutDependency to false.");

            yield return next;

            await invalidation;
        }
    }

    private class AsyncInvalidation(CancellationToken cancellationToken) : IDependentCacheEntry
    {
        private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        public bool HasDependencies { get; private set; }

        public void AddTaint((string Key, object Value) taint) { }

        public void Invalidate() => _cts.Cancel();

        public void OnDependencyAdded(IDependentCacheEntry dependency) => HasDependencies = true;

        public ConfiguredTaskAwaitable.ConfiguredTaskAwaiter GetAwaiter() => Task
            .Delay(Timeout.Infinite, _cts.Token)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
            .GetAwaiter();

    }
}
