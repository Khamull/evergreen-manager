using Evergreen.Domain;
using Evergreen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Evergreen.Application;

public sealed class ArticleImportService(EvergreenDbContext db, IArticleCrawler crawler)
{
    public async Task<(int Discovered, int Imported)> ImportAsync(Uri site, Guid tenantId, CancellationToken ct = default)
    {
        var found = await crawler.DiscoverAsync(site, tenantId, ct);
        var imported = 0;
        foreach (var item in found)
        {
            var existing = await db.Articles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.SourceKey == item.SourceKey, ct);
            if (existing is null) { db.Articles.Add(item); imported++; }
            else
            {
                existing.Title = item.Title; existing.Url = item.Url; existing.Slug = item.Slug; existing.Excerpt = item.Excerpt;
                existing.ContentHtml = item.ContentHtml; existing.AuthorName = item.AuthorName; existing.AuthorUrl = item.AuthorUrl;
                existing.PublishedAt = item.PublishedAt; existing.ModifiedAt = item.ModifiedAt; existing.FeaturedImageUrl = item.FeaturedImageUrl;
                existing.Categories = item.Categories; existing.Tags = item.Tags; existing.SeoDescription = item.SeoDescription;
                existing.LastSeenAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
        return (found.Count, imported);
    }
}
