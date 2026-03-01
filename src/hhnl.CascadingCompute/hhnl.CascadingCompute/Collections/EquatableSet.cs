namespace hhnl.CascadingCompute.Collections;

public class EquatableSet<T> : HashSet<T>
{
    public EquatableSet() : base() { }
    public EquatableSet(IEnumerable<T> collection) : base(collection) { }
    public EquatableSet(IEqualityComparer<T> comparer) : base(comparer) { }
    public EquatableSet(int capacity) : base(capacity) { }
    public EquatableSet(IEnumerable<T> collection, IEqualityComparer<T> comparer) : base(collection, comparer) { }
    public EquatableSet(int capacity, IEqualityComparer<T> comparer) : base(capacity, comparer) { }
    public override bool Equals(object? obj)
    {
        if (obj is not HashSet<T> other || Count != other.Count)
            return false;
        return SetEquals(other);
    }
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var item in this)
        {
            hash ^= item?.GetHashCode() ?? 0;
        }
        return hash;
    }

    public static bool operator ==(EquatableSet<T>? left, EquatableSet<T>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(EquatableSet<T>? left, EquatableSet<T>? right)
    {
        return !(left == right);
    }
}
