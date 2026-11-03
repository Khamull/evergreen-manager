using Evergreen.Application;
using Evergreen.Domain;
using Evergreen.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Evergreen.Tests.Application;

public sealed class ArticleImportServiceTests
{
    [Fact]
    public async Task ImportAsync_IsIdempotentAndUpdatesExistingArticle()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<EvergreenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new EvergreenDbContext(options, new TenantContext { TenantId = tenantId });
        db.Articles.Add(new Article { TenantId = tenantId, SourceKey = "post-1", Url = "https://example.org/1", Title = "Old title" });
        await db.SaveChangesAsync();
        var crawler = new FakeCrawler([new Article { TenantId = tenantId, SourceKey = "post-1", Url = "https://example.org/1", Title = "Updated title" }, new Article { TenantId = tenantId, SourceKey = "post-2", Url = "https://example.org/2", Title = "New article" }]);
        var sut = new ArticleImportService(db, crawler);
        var result = await sut.ImportAsync(new Uri("https://example.org"), tenantId);
        result.Discovered.Should().Be(2);
        result.Imported.Should().Be(1);
        (await db.Articles.OrderBy(x => x.SourceKey).ToListAsync()).Should().HaveCount(2);
        (await db.Articles.SingleAsync(x => x.SourceKey == "post-1")).Title.Should().Be("Updated title");
    }

    private sealed class FakeCrawler(IReadOnlyList<Article> articles) : IArticleCrawler
    {
        public Task<IReadOnlyList<Article>> DiscoverAsync(Uri site, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(articles);
    }
}
