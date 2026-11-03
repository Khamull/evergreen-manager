using System.Net;
using Evergreen.Application;
using FluentAssertions;

namespace Evergreen.Tests.Infrastructure;

public sealed class WordPressArticleCrawlerTests
{
    [Fact]
    public async Task DiscoverAsync_ParsesWordPressRestPostsAndMetadata()
    {
        var json = """[{"id":42,"date":"2025-04-01T10:00:00","date_gmt":"2025-04-01T09:00:00","modified":"2025-04-02T10:00:00","slug":"artigo-teste","link":"https://example.org/artigo-teste/","title":{"rendered":"Artigo <em>Teste</em>"},"excerpt":{"rendered":"<p>Resumo</p>"},"content":{"rendered":"<p>Conteúdo integral</p>"},"_embedded":{"author":[{"name":"Marco Milani","link":"https://example.org/author/marco/"}],"wp:featuredmedia":[{"source_url":"https://example.org/image.jpg"}],"wp:term":[[{"name":"Estudos"}],[{"name":"Kardec"}]]}}]""";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var crawler = new WordPressArticleCrawler(new HttpClient(handler));
        var tenant = Guid.NewGuid();
        var articles = await crawler.DiscoverAsync(new Uri("https://example.org"), tenant);
        articles.Should().ContainSingle();
        articles[0].TenantId.Should().Be(tenant);
        articles[0].Title.Should().Be("Artigo Teste");
        articles[0].AuthorName.Should().Be("Marco Milani");
        articles[0].Categories.Should().Contain("Estudos");
        articles[0].Tags.Should().Contain("Kardec");
        articles[0].FeaturedImageUrl.Should().Be("https://example.org/image.jpg");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
