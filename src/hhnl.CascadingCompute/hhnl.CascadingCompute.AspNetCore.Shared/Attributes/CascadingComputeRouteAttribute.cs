namespace hhnl.CascadingCompute.AspNetCore.Shared.Attributes;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
public sealed class CascadingComputeRouteAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}
