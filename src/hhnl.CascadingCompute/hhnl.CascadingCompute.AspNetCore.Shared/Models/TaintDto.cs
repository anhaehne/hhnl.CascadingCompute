namespace hhnl.CascadingCompute.AspNetCore.Shared.Models;

public class TaintDto(string key, object value)
{
    public string Key { get; } = key;
    public object Value { get; } = value;
}
