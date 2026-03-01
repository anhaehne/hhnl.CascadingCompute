using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Assembly, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreParameterAttribute : ParameterExclusionAttribute
{
    private readonly string? _parameterName;
    private readonly Type? _type;

    public IgnoreParameterAttribute(string parameterName)
    {
        _parameterName = parameterName;
    }

    public IgnoreParameterAttribute(Type type)
    {
        _type = type;
    }

    public IgnoreParameterAttribute(Type type, string parameterName)
    {
        _type = type;
        _parameterName = parameterName;
    }

    public override bool ShouldExcludeParameter(Type parameterType, string parameterName, string methodName, Type methodDeclaringType)
    {
        if (_type is null && parameterType is null)
            throw new InvalidOperationException("At least one of the parameters must be provided.");

        if (_type != null && _type != parameterType)
        {
            return false;
        }

        if (_parameterName != null && _parameterName != parameterName)
        {
            return false;
        }

        return true;
    }
}
