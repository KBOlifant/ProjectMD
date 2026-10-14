using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Extraction;
using static ProjectMD.Api.Tests.Extraction.ExtractionTestSupport;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the extraction service, its DI registration, and the fetcher's checks that happen
// before any browser is needed. No network and no browser.
public class ExtractionServiceTests
{
    // A fetcher that returns a canned page and remembers what it was asked.
    private sealed class FakeFetcher : IPageFetcher
    {
        private readonly Func<Uri, RawPage> _respond;

        public FakeFetcher(Func<Uri, RawPage> respond) => _respond = respond;

        public List<Uri> Requested { get; } = [];

        public Task<RawPage> FetchAsync(Uri url, CancellationToken ct)
        {
            Requested.Add(url);
            return Task.FromResult(_respond(url));
        }
    }

    // Builds the service around a fake fetcher and the real reducer.
    private static (ExtractionService Service, FakeFetcher Fetcher) Build(Func<Uri, RawPage> respond, ExtractionOptions? options = null)
    {
        var fetcher = new FakeFetcher(respond);
        return (new ExtractionService(fetcher, new HtmlReducer(), Options.Create(options ?? new ExtractionOptions())), fetcher);
    }

    // The service fetches the URL and returns the reduced digest.
    [Fact]
    public async Task Fetches_then_reduces()
    {
        var (service, fetcher) = Build(_ => Raw(SamplePage()));

        var digest = await service.ExtractAsync(new Uri("https://example-site.com/"), CancellationToken.None);

        Assert.Equal(new Uri("https://example-site.com/"), Assert.Single(fetcher.Requested));
        Assert.Equal("Acme Docs | Getting started", digest.Title);
        Assert.True(digest.ReducedChars < digest.RawHtmlChars);
    }

    // The reducer limits come from the settings.
    [Fact]
    public async Task Uses_configured_reducer_limits()
    {
        var options = new ExtractionOptions { MaxTextChars = 500, MaxLinks = 2, MaxHeadings = 1 };
        var (service, _) = Build(_ => Raw(SamplePage()), options);

        var digest = await service.ExtractAsync(new Uri("https://example-site.com/"), CancellationToken.None);

        Assert.True(digest.Text.Length <= 500);
        Assert.Equal(2, digest.Links.Count);
        Assert.Single(digest.Outline);
    }

    // A site that answers with an error status is an error, not a page to document.
    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task Http_errors_are_rejected_with_a_safe_message(int status)
    {
        var (service, _) = Build(_ => Raw("<body>Not found</body>", status: status));

        var ex = await Assert.ThrowsAsync<ExtractionException>(() => service.ExtractAsync(new Uri("https://example-site.com/missing"), CancellationToken.None));

        Assert.Equal(ExtractionFailure.HttpError, ex.Failure);
        Assert.Contains(status.ToString(), ex.Message);
        Assert.DoesNotContain("example-site.com", ex.Message);
    }

    // The error check can be turned off, and redirects (3xx) and success codes are fine.
    [Fact]
    public async Task Http_error_rejection_can_be_turned_off()
    {
        var (service, _) = Build(_ => Raw("<body><p>Error page text that is long enough.</p></body>", status: 404), new ExtractionOptions { RejectHttpErrors = false });

        var digest = await service.ExtractAsync(new Uri("https://example-site.com/missing"), CancellationToken.None);

        Assert.Equal(404, digest.StatusCode);
    }

    // Fetch failures pass through unchanged.
    [Fact]
    public async Task Fetch_failures_pass_through()
    {
        var failing = new ExtractionException(ExtractionFailure.TimedOut, "slow");
        var service = new ExtractionService(new ThrowingFetcher(failing), new HtmlReducer(), Options.Create(new ExtractionOptions()));

        var thrown = await Assert.ThrowsAsync<ExtractionException>(() => service.ExtractAsync(new Uri("https://example-site.com/"), CancellationToken.None));

        Assert.Same(failing, thrown);
    }

    // A fetcher that always fails.
    private sealed class ThrowingFetcher : IPageFetcher
    {
        private readonly Exception _error;

        public ThrowingFetcher(Exception error) => _error = error;

        public Task<RawPage> FetchAsync(Uri url, CancellationToken ct) => throw _error;
    }

    // AddExtraction registers everything and binds the settings, without starting a browser.
    [Fact]
    public async Task AddExtraction_registers_the_services_and_binds_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scanner:MaxConcurrentFetches"] = "3",
                ["Scanner:MaxHtmlBytes"] = "1000000",
                ["Scanner:Mode"] = "extract",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUrlValidator, UrlValidator>();
        services.AddExtraction(configuration);
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var options = provider.GetRequiredService<IOptions<ExtractionOptions>>().Value;
        Assert.Equal(3, options.MaxConcurrentFetches);
        Assert.Equal(1_000_000, options.MaxHtmlBytes);
        Assert.Equal(30, options.NavigationTimeoutSeconds);
        Assert.Equal(45, options.OverallTimeoutSeconds);

        Assert.IsType<ExtractionService>(provider.GetRequiredService<IExtractionService>());
        Assert.IsType<PlaywrightPageFetcher>(provider.GetRequiredService<IPageFetcher>());
        Assert.IsType<HtmlReducer>(provider.GetRequiredService<IHtmlReducer>());
        Assert.IsType<TechFingerprinter>(provider.GetRequiredService<ITechFingerprinter>());
        Assert.Same(provider.GetRequiredService<IExtractionService>(), provider.GetRequiredService<IExtractionService>());
    }

    // Defaults match the documented ones when nothing is configured.
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        var options = new ExtractionOptions();

        Assert.Equal(2, options.MaxConcurrentFetches);
        Assert.Equal(2 * 1024 * 1024, options.MaxHtmlBytes);
        Assert.Equal(30, options.NavigationTimeoutSeconds);
        Assert.Equal(45, options.OverallTimeoutSeconds);
        Assert.Equal(12000, options.MaxTextChars);
        Assert.Equal(40, options.MaxLinks);
        Assert.Equal(60, options.MaxHeadings);
        Assert.True(ExtractionOptions.IsValid(options));
    }

    // Out-of-range settings are rejected when the options are read.
    [Fact]
    public void Invalid_settings_are_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Scanner:MaxConcurrentFetches"] = "0" })
            .Build();
        var services = new ServiceCollection();
        services.AddExtraction(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ExtractionOptions>>().Value);
    }

    // A registration made before AddExtraction wins (so tests can swap the fetcher).
    [Fact]
    public void Existing_registrations_are_kept()
    {
        var services = new ServiceCollection();
        var fake = new FakeFetcher(_ => Raw("<body></body>"));
        services.AddSingleton<IPageFetcher>(fake);
        services.AddExtraction(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IPageFetcher>());
    }

    // The fetcher refuses private and malformed URLs by the URL rules before it needs a browser.
    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("ftp://example.com/")]
    [InlineData("https://user:pass@example.com/")]
    public async Task Fetcher_refuses_non_public_urls_without_a_browser(string url)
    {
        await using var fetcher = new PlaywrightPageFetcher(
            new UrlValidator(), new FakeDns(), Options.Create(new ExtractionOptions()), NullLogger<PlaywrightPageFetcher>.Instance);

        var ex = await Assert.ThrowsAsync<ExtractionException>(() => fetcher.FetchAsync(new Uri(url), CancellationToken.None));

        Assert.Equal(ExtractionFailure.BlockedAddress, ex.Failure);
        Assert.True(ex.IsClientError);
    }
}
