using ProjectMD.Api.Models;

namespace ProjectMD.Api.Services;

// What a generator produces for one URL. The controller adds the id, owner, URL, host and
// timestamp when it saves the scan.
public sealed record GeneratedScan(
    int DurationMs,
    int Confidence,
    int Pages,
    IReadOnlyList<ScanTech> Tech,
    string Markdown);

// Turns an already-validated public URL into the documentation for a scan. Today the only
// implementation is StaticScanGenerator (MOCK data). A Groq-backed generator will replace it
// later; that one must read its API key from server-side configuration only (never from the
// request, the repo or a log), and must re-check the URL after DNS resolution before fetching.
// Nothing here calls any AI service, makes any HTTP request or handles any key yet.
public interface IScanGenerator
{
    // Builds the scan content for the URL. The URL has already passed IUrlValidator.
    Task<GeneratedScan> GenerateAsync(Uri url, CancellationToken ct);
}
