using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProjectMD.Api.Services.Extraction;

// One call that registers the extraction pieces. AddScanServices calls it.
public static class ExtractionServiceCollectionExtensions
{
    // Registers the settings (section "Scanner"), the DNS resolver, the page fetcher, the HTML
    // reducer, the tech fingerprinter and the extraction service. Everything is a singleton. The
    // browser is NOT started here: the fetcher starts it on first use and shuts it down when the app
    // stops, so merely registering this costs nothing in the `static` scanner mode. Registrations
    // use TryAdd, so a test can register a fake first. IUrlValidator must be registered elsewhere
    // (AddScanServices does it).
    public static IServiceCollection AddExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExtractionOptions>()
            .Bind(configuration.GetSection(ExtractionOptions.SectionName))
            .Validate(ExtractionOptions.IsValid, "Scanner settings are out of range (see backend/README.md, Page extraction).");

        services.TryAddSingleton<IDnsResolver, SystemDnsResolver>();
        services.TryAddSingleton<IPageFetcher, PlaywrightPageFetcher>();
        services.TryAddSingleton<IHtmlReducer, HtmlReducer>();
        services.TryAddSingleton(_ => TechRuleSet.LoadEmbedded());
        services.TryAddSingleton<ITechFingerprinter, TechFingerprinter>();
        services.TryAddSingleton<IExtractionService, ExtractionService>();

        return services;
    }
}
