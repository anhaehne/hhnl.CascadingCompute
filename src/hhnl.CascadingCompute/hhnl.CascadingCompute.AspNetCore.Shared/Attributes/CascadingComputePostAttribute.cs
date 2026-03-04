namespace hhnl.CascadingCompute.AspNetCore.Shared.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CascadingComputePostAttribute(string template = "") : Attribute
{
    public string Template { get; } = template;
}
