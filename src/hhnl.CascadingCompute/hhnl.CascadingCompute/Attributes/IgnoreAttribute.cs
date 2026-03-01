using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreAttribute : ParameterExclusionAttribute
{
    public override bool ShouldExcludeParameter(Type parameterType, string parameterName, string methodName, Type methodDeclaringType)
    {
        // Implement your logic to determine if the parameter should be excluded
        return true;
    }
}
