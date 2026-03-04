namespace hhnl.CascadingCompute.AspNetCore.Shared.Models;


public class InvalidationDto(string url, IReadOnlyCollection<TaintDto> taints)
{
    public string Url { get; } = url;
    public IReadOnlyCollection<TaintDto> Taints { get; } = taints;
}