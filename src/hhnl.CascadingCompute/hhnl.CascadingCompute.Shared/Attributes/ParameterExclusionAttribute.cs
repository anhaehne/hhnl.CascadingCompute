namespace hhnl.CascadingCompute.Shared.Attributes;

public abstract class ParameterExclusionAttribute : Attribute
{
    /// <summary>
    /// Determines whether the specified parameter should be excluded from the cache key.
    /// </summary>
    public abstract bool ShouldExcludeParameter(Type parameterType, string parameterName, string methodName, Type methodDeclaringType);
}
