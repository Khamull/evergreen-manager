using System.Security.Claims;
using Evergreen.Infrastructure;
using Evergreen.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Evergreen.Tests.Application;

public sealed class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UsesTenantClaimFromAuthenticatedPrincipal()
    {
        var expected = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", expected.ToString())], "test"));
        var tenant = new TenantContext();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenant);
        tenant.TenantId.Should().Be(expected);
    }

    [Fact]
    public async Task InvokeAsync_LeavesTenantUnsetWhenClaimIsMissing()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var tenant = new TenantContext();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenant);
        tenant.TenantId.Should().BeNull();
    }
}
