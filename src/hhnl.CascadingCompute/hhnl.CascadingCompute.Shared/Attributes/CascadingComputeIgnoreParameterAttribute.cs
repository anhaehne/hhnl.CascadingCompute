namespace hhnl.CascadingCompute.Attributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Assembly, AllowMultiple = true, Inherited = true)]
public sealed class CascadingComputeIgnoreParameterAttribute : Attribute
{
    public CascadingComputeIgnoreParameterAttribute()
    {
    }

    public CascadingComputeIgnoreParameterAttribute(string parameterName)
    {
        ParameterName = parameterName;
    }

    public CascadingComputeIgnoreParameterAttribute(Type type)
    {
        Type = type;
    }

    public CascadingComputeIgnoreParameterAttribute(Type type, string parameterName)
    {
        Type = type;
        ParameterName = parameterName;
    }

    public string? ParameterName { get; }

    public Type? Type { get; }
}
