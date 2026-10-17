using Microsoft.Extensions.Options;
using ProjectMD.Api.Services.Extraction;
using static ProjectMD.Api.Tests.Extraction.ExtractionTestSupport;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for recognising bot-check pages (Cloudflare and similar) and for the clear error the
// extraction service gives for them. No network and no browser.
public class BlockDetectorTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    private const string CloudflarePage =
        "<html><head><title>Attention Required! | Cloudflare</title></head><body><h1>Sorry, you have been blocked</h1></body></html>";

    // A fetcher that always returns the same page.
    private sealed class FixedFetcher(RawPage page) : IPageFetcher
    {
        public Task<RawPage> FetchAsync(Uri url, CancellationToken ct) => Task.FromResult(page);
    }

    [Fact]
    public void Cloudflare_block_page_is_recognised()
    {
        Assert.True(BlockDetector.LooksBlocked(403, NoHeaders, CloudflarePage));
    }

    [Fact]
    public void Cloudflare_challenge_header_is_enough_on_its_own()
    {
        var headers = new Dictionary<string, string> { ["cf-mitigated"] = "challenge" };

        Assert.True(BlockDetector.LooksBlocked(200, headers, "<html><body>Loading</body></html>"));
    }

    [Theory]
    [InlineData("Just a moment...")]
    [InlineData("Access Denied")]
    [InlineData("Pardon Our Interruption")]
    public void Common_challenge_titles_are_recognised_on_a_small_error_page(string title)
    {
        var html = $"<html><head><title>{title}</title></head><body>check</body></html>";

        Assert.True(BlockDetector.LooksBlocked(503, NoHeaders, html));
    }

    [Fact]
    public void A_normal_page_is_not_blocked()
    {
        Assert.False(BlockDetector.LooksBlocked(200, NoHeaders, SamplePage()));
    }

    [Fact]
    public void A_long_real_article_titled_access_denied_is_not_treated_as_a_block()
    {
        var html = "<html><head><title>Access denied: a short history of locks</title></head><body><p>"
            + new string('x', 80_000) + "</p></body></html>";

        Assert.False(BlockDetector.LooksBlocked(200, NoHeaders, html));
    }

    [Fact]
    public void Title_is_read_decoded_and_in_lower_case()
    {
        Assert.Equal("just a moment...", BlockDetector.Title("<title>\n  Just a&#32;moment...  </title>"));
        Assert.Null(BlockDetector.Title("<html><body>no title</body></html>"));
    }

    [Fact]
    public async Task Extraction_service_refuses_a_block_page_with_a_clear_message()
    {
        var service = new ExtractionService(
            new FixedFetcher(Raw(CloudflarePage, status: 403)),
            new HtmlReducer(),
            Options.Create(new ExtractionOptions()));

        var ex = await Assert.ThrowsAsync<ExtractionException>(
            () => service.ExtractAsync(new Uri("https://example-site.com/"), CancellationToken.None));

        Assert.Equal(ExtractionFailure.BlockedBySite, ex.Failure);
        Assert.Equal(FetchSupport.BlockedBySiteMessage, ex.Message);
        Assert.False(ex.IsClientError);
    }

    [Fact]
    public void The_cloudflare_challenge_header_survives_header_filtering()
    {
        var kept = FetchSupport.FilterHeaders(new Dictionary<string, string> { ["CF-Mitigated"] = "challenge" });

        Assert.Equal("challenge", kept["cf-mitigated"]);
    }
}
