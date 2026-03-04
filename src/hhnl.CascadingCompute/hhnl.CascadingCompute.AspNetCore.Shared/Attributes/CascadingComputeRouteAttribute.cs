using hhnl.CascadingCompute.AspNetCore.Shared.Enums;

namespace hhnl.CascadingCompute.AspNetCore.Shared.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CascadingComputeRouteAttribute(string template, CascadingComputeHttpMethod method = CascadingComputeHttpMethod.Get) : Attribute
{
    public string Template { get; } = template;

    public CascadingComputeHttpMethod Method { get; } = method;
}
