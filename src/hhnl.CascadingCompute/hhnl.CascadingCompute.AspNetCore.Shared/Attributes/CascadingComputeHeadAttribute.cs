namespace hhnl.CascadingCompute.AspNetCore.Shared.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CascadingComputeHeadAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}
