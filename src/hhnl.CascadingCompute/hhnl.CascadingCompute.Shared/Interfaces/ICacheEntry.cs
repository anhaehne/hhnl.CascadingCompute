namespace hhnl.CascadingCompute.Shared.Interfaces;

public interface ICacheEntry<TResult>
{
    TResult Value { get; set; }

    DateTimeOffset CreatedAt { get; }

    void Invalidate();
}