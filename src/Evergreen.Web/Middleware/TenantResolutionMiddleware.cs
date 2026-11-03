using System.Security.Claims;
using Evergreen.Infrastructure;

namespace Evergreen.Middleware;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        var claim = context.User.FindFirstValue("tenant_id");
        if (Guid.TryParse(claim, out var tenantId)) tenant.TenantId = tenantId;
        await next(context);
    }
}
