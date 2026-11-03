using System.Net;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using Evergreen.Domain;

namespace Evergreen.Application;

public interface IArticleCrawler { Task<IReadOnlyList<Article>> DiscoverAsync(Uri site, Guid tenantId, CancellationToken cancellationToken = default); }

/// <summary>WordPress REST API first; sitemap + HTML metadata fallback.</summary>
public sealed class WordPressArticleCrawler(HttpClient http) : IArticleCrawler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IBrowsingContext _browser = BrowsingContext.New(Configuration.Default);

    public async Task<IReadOnlyList<Article>> DiscoverAsync(Uri site, Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var api = await DiscoverViaRestAsync(site, tenantId, cancellationToken);
            if (api.Count > 0) return api;
        }
        catch (HttpRequestException) { }
        catch (JsonException) { }
        return await DiscoverViaSitemapAsync(site, tenantId, cancellationToken);
    }

    private async Task<IReadOnlyList<Article>> DiscoverViaRestAsync(Uri site, Guid tenantId, CancellationToken ct)
    {
        var articles = new List<Article>();
        for (var page = 1; page <= 100; page++)
        {
            var url = new Uri(site, $"/wp-json/wp/v2/posts?per_page=100&page={page}&_embed=true&status=publish");
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound && page == 1) return [];
            if (response.StatusCode == HttpStatusCode.BadRequest && page > 1) break;
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var posts = JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOptions) ?? [];
            if (posts.Count == 0) break;
            foreach (var post in posts)
            {
                var title = Rendered(post, "title");
                var link = String(post, "link");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;
                var embedded = Get(post, "_embedded");
                var author = FirstEmbedded(embedded, "author");
                var media = FirstEmbedded(embedded, "wp:featuredmedia");
                var cats = EmbeddedNames(embedded, "wp:term", 0);
                var tags = EmbeddedNames(embedded, "wp:term", 1);
                articles.Add(new Article {
                    TenantId = tenantId, SourceKey = link, Url = link, Title = CleanText(title),
                    Slug = String(post, "slug"), Excerpt = Rendered(post, "excerpt"), ContentHtml = Rendered(post, "content"),
                    AuthorName = String(author, "name"), AuthorUrl = String(author, "link"),
                    PublishedAt = ParseDate(String(post, "date_gmt") ?? String(post, "date")),
                    ModifiedAt = ParseDate(String(post, "modified_gmt") ?? String(post, "modified")),
                    FeaturedImageUrl = String(Get(media, "source_url")), Categories = cats, Tags = tags,
                    CanonicalUrl = link, MetaTitle = CleanText(title), SeoDescription = Rendered(post, "excerpt")
                });
            }
            if (posts.Count < 100) break;
        }
        return articles;
    }

    private async Task<IReadOnlyList<Article>> DiscoverViaSitemapAsync(Uri site, Guid tenantId, CancellationToken ct)
    {
        var sitemapCandidates = new[] { new Uri(site, "/wp-sitemap-posts-post-1.xml"), new Uri(site, "/sitemap_index.xml"), new Uri(site, "/wp-sitemap.xml") };
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sitemap in sitemapCandidates)
        {
            await ReadSitemapAsync(sitemap, site, urls, visitedSitemaps, 0, ct);
            if (urls.Count > 0) break;
        }
        var result = new List<Article>();
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var html = await http.GetStringAsync(url, ct);
                var doc = await _browser.OpenAsync(req => req.Content(html), ct);
                var title = Meta(doc, "og:title") ?? doc.QuerySelector("h1")?.TextContent.Trim() ?? doc.Title;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var canonical = doc.QuerySelector("link[rel=canonical]")?.GetAttribute("href") ?? url;
                result.Add(new Article {
                    TenantId = tenantId, SourceKey = canonical, Url = url, CanonicalUrl = canonical, Title = CleanText(title),
                    AuthorName = Meta(doc, "author") ?? doc.QuerySelector(".author a, .byline a")?.TextContent.Trim(),
                    PublishedAt = ParseDate(Meta(doc, "article:published_time") ?? Meta(doc, "datePublished")),
                    ModifiedAt = ParseDate(Meta(doc, "article:modified_time") ?? Meta(doc, "dateModified")),
                    FeaturedImageUrl = Meta(doc, "og:image"), SeoDescription = Meta(doc, "description"),
                    ContentHtml = doc.QuerySelector("article")?.InnerHtml,
                    Categories = doc.QuerySelectorAll(".cat-links a, .categories a").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).Distinct().ToArray(),
                    Tags = doc.QuerySelectorAll(".tags-links a, .tagcloud a").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).Distinct().ToArray()
                });
            }
            catch (HttpRequestException) { /* Continue with other sitemap entries. */ }
        }
        return result;
    }


    private async Task ReadSitemapAsync(Uri sitemap, Uri site, HashSet<string> urls, HashSet<string> visited, int depth, CancellationToken ct)
    {
        if (depth > 2 || !visited.Add(sitemap.ToString())) return;
        try
        {
            var xml = await http.GetStringAsync(sitemap, ct);
            var doc = await _browser.OpenAsync(req => req.Content(xml), ct);
            foreach (var loc in doc.QuerySelectorAll("loc"))
            {
                if (!Uri.TryCreate(loc.TextContent.Trim(), UriKind.Absolute, out var parsed) || parsed.Host != site.Host) continue;
                var path = parsed.AbsolutePath;
                if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || path.Contains("sitemap", StringComparison.OrdinalIgnoreCase))
                    await ReadSitemapAsync(parsed, site, urls, visited, depth + 1, ct);
                else
                    urls.Add(parsed.ToString());
            }
        }
        catch (HttpRequestException) { }
    }

    private static JsonElement Get(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static string? String(JsonElement element, string? name = null) { if (name is not null) element = Get(element, name); return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ValueKind == JsonValueKind.Number ? element.ToString() : null; }
    private static string? Rendered(JsonElement element, string name) => String(Get(Get(element, name), "rendered"));
    private static JsonElement FirstEmbedded(JsonElement embedded, string key) { var array = Get(embedded, key); return array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0 ? array[0] : default; }
    private static string[] EmbeddedNames(JsonElement embedded, string key, int index) { var terms = Get(embedded, key); if (terms.ValueKind != JsonValueKind.Array || terms.GetArrayLength() <= index || terms[index].ValueKind != JsonValueKind.Array) return []; return terms[index].EnumerateArray().Select(x => String(x, "name")).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct().ToArray(); }
    private static string CleanText(string value) => System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ")), @"\s+", " ").Trim();
    private static string? Meta(IDocument doc, string name) => doc.QuerySelector($"meta[property='{name}']")?.GetAttribute("content") ?? doc.QuerySelector($"meta[name='{name}']")?.GetAttribute("content");
    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}
