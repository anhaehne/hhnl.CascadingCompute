namespace hhnl.CascadingCompute.Shared.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class CascadingComputeAttribute(int autoInvalidateInMilliseconds = 0) : Attribute
{
    public int AutoInvalidateInMilliseconds { get; } = autoInvalidateInMilliseconds;
}
