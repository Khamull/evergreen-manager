namespace Evergreen.Infrastructure;

public interface ITenantContext
{
    Guid? TenantId { get; set; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}
