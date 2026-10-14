using ProjectMD.Api.Models;

namespace ProjectMD.Api.Services.Extraction;

// Detects which technologies a page uses (frameworks, hosts, analytics and so on) from the digest
// alone, by fixed rules. No network and no AI.
public interface ITechFingerprinter
{
    // At most 8 technologies; the framework/host come first and are the ones marked Highlighted.
    IReadOnlyList<ScanTech> Detect(PageDigest digest);
}

// The free front half of a scan: fetch a public URL with the browser and reduce it to a PageDigest.
public interface IExtractionService
{
    // Fetches and reduces the page. Throws ExtractionException (safe message) on failure.
    Task<PageDigest> ExtractAsync(Uri url, CancellationToken ct);
}
