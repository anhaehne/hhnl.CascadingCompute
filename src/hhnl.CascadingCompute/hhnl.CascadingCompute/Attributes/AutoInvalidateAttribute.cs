using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;
using System.Runtime.CompilerServices;

namespace hhnl.CascadingCompute.Attributes;

public sealed class AutoInvalidateAttribute : CacheEntryLifetimeObserverAttribute
{
    private readonly long _dueTime;

    private readonly ConditionalWeakTable<object, Timer> _timers = [];

    public AutoInvalidateAttribute(int timeInMilliseconds)
    {
        if (timeInMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeInMilliseconds), "Time in milliseconds must be greater than zero.");

        _dueTime = timeInMilliseconds;
    }

    public AutoInvalidateAttribute(int years = 0, int months = 0, int days = 0, int hours = 0, int minutes = 0, int seconds = 0, int milliseconds = 0)
    {
        var timeSpan = new TimeSpan(days + years * 365 + months * 30, hours, minutes, seconds, milliseconds);
        if (timeSpan <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException("Time span must be greater than zero.");
        _dueTime = (long)timeSpan.TotalMilliseconds;
    }

    public override void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
    {
        var timer = new Timer(
            static state => ((Action)state!).Invoke(),
            (Action)cacheEntry.Invalidate,
            _dueTime,
            Timeout.Infinite);

        _timers.Add(cacheEntry, timer);
    }

    public override void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
    {
        if (_timers.TryGetValue(cacheEntry, out var timer))
        {
            timer.Dispose();
            _timers.Remove(cacheEntry);
        }
    }
}
