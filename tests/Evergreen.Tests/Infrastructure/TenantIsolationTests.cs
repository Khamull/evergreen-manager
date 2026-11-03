using Evergreen.Domain;
using Evergreen.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Evergreen.Tests.Infrastructure;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Articles_AreFilteredByCurrentTenant()
    {
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<EvergreenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var contextA = new TenantContext { TenantId = tenantA };
        await using (var seed = new EvergreenDbContext(options, new TenantContext()))
        {
            seed.Articles.AddRange(new Article { TenantId = tenantA, SourceKey = "a", Url = "https://a.test", Title = "Tenant A" }, new Article { TenantId = tenantB, SourceKey = "b", Url = "https://b.test", Title = "Tenant B" });
            await seed.SaveChangesAsync();
        }
        await using var db = new EvergreenDbContext(options, contextA);
        var articles = await db.Articles.ToListAsync();
        articles.Should().ContainSingle().Which.Title.Should().Be("Tenant A");
    }
}
