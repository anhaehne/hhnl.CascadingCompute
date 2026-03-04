namespace TestProject.Server.Context;

public interface ITenantContextAccessor
{
    string TenantId { get; set; }
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<string?> CurrentTenant = new();

    public string TenantId
    {
        get => CurrentTenant.Value ?? "default";
        set => CurrentTenant.Value = string.IsNullOrWhiteSpace(value) ? "default" : value;
    }
}
