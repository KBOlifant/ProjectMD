using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;
using ProjectMD.Api.Tests.Scans;

namespace ProjectMD.Api.Tests.Ai;

// Builds PageDigest values for tests, without a browser or any HTML.
internal static class Digests
{
    // A small, ordinary page. Pass a different `hash` to simulate different content.
    public static PageDigest Simple(string hash = "hash-one", string title = "Acme Cloud", string? text = null) => new(
        FinalUrl: new Uri("https://acme.example/"),
        StatusCode: 200,
        Title: title,
        MetaDescription: "Acme Cloud runs your apps close to your users.",
        Language: "en",
        Outline: [new HeadingNode(1, "Acme Cloud"), new HeadingNode(2, "Pricing"), new HeadingNode(2, "Docs")],
        Text: text ?? "Acme Cloud runs your apps close to your users.\nDeploy in seconds.\nPricing starts free.",
        Links: [new LinkRef("Pricing", "https://acme.example/pricing"), new LinkRef("Docs", "https://acme.example/docs")],
        ScriptUrls: ["https://acme.example/app.js"],
        StylesheetUrls: [],
        Headers: new Dictionary<string, string>(),
        JsonLd: ["{\"@type\":\"Organization\",\"name\":\"Acme\"}"],
        RawHtmlChars: 50_000,
        ReducedChars: 120,
        EstimatedTokens: 30,
        ContentHash: hash);

    // A very large page: long text, many headings, many links and lots of structured data.
    public static PageDigest Huge(string hash = "hash-huge", string textUnit = "lorem ipsum dolor sit amet ")
    {
        var text = string.Concat(Enumerable.Repeat(textUnit, 4000));
        return Simple(hash, "Huge page", text) with
        {
            Outline = Enumerable.Range(0, 60).Select(i => new HeadingNode(2 + i % 3, $"Heading number {i} about things")).ToList(),
            Links = Enumerable.Range(0, 40).Select(i => new LinkRef($"Link {i}", $"https://acme.example/page-{i}")).ToList(),
            JsonLd = Enumerable.Range(0, 10).Select(i => "{\"@type\":\"Thing\",\"name\":\"" + new string('x', 400) + i + "\"}").ToList(),
        };
    }
}

// A fake AI client that counts calls and answers (or fails) as the test says.
internal sealed class FakeLlmClient : ILlmClient
{
    private readonly Func<int, LlmRequest, LlmResult> _respond;

    // Every request received, in order.
    public List<LlmRequest> Requests { get; } = [];

    // How many times the model was called.
    public int Calls => Requests.Count;

    // Answers every call with the given function (1-based call number and the request).
    public FakeLlmClient(Func<int, LlmRequest, LlmResult> respond)
    {
        _respond = respond;
    }

    // A client that always answers with the same prose and token counts.
    public static FakeLlmClient Answering(string text = "## Summary\nAcme runs apps.\n## What the site does\n- Hosts apps", int prompt = 800, int completion = 150) =>
        new((_, request) => new LlmResult(text, prompt, completion, request.Model));

    // A client that always throws the given exception.
    public static FakeLlmClient Throwing(Exception exception) =>
        new((_, _) => throw exception);

    // Records the request and returns what the test function says.
    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(Requests.Count, request));
    }
}

// A fake extraction service that returns a fixed digest (or throws) and counts calls.
internal sealed class FakeExtractionService : IExtractionService
{
    private readonly Func<PageDigest> _digest;

    // How many times a page was "fetched".
    public int Calls { get; private set; }

    // Returns the digest produced by the function (which may throw).
    public FakeExtractionService(Func<PageDigest> digest)
    {
        _digest = digest;
    }

    // Always returns the same digest.
    public FakeExtractionService(PageDigest digest)
        : this(() => digest)
    {
    }

    // Counts the call, then returns (or throws).
    public Task<PageDigest> ExtractAsync(Uri url, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_digest());
    }
}

// A fake fingerprinter that returns a fixed tech list.
internal sealed class FakeFingerprinter : ITechFingerprinter
{
    private readonly IReadOnlyList<ScanTech> _tech;

    // Always returns the given technologies.
    public FakeFingerprinter(params ScanTech[] tech)
    {
        _tech = tech;
    }

    // Returns the fixed list.
    public IReadOnlyList<ScanTech> Detect(PageDigest digest) => _tech;
}

// A fixed owner for the scan.
internal sealed class FakeOwner : IScanOwnerProvider
{
    private readonly Guid _userId;

    // Always answers with this user id.
    public FakeOwner(Guid userId)
    {
        _userId = userId;
    }

    // Returns the fixed id.
    public Task<Guid> GetUserIdAsync(CancellationToken ct) => Task.FromResult(_userId);
}

// Everything a pipeline test needs, wired with an in-memory SQLite database and fakes. Nothing here
// touches the network or any real database.
internal sealed class PipelineHarness : IDisposable
{
    public TestDatabase Db { get; } = new();
    public AppUser User { get; }
    public FakeExtractionService Extraction { get; }
    public FakeLlmClient? FakeLlm { get; }
    public AiOptions Ai { get; }
    public TimeProvider Clock { get; }
    public ILlmClient Llm { get; }
    public ScannerMode Mode { get; }

    // Creates the harness for a mode, with a client (null = the real NotConfiguredLlmClient).
    public PipelineHarness(
        ScannerMode mode,
        FakeLlmClient? llm,
        PageDigest? digest = null,
        Action<AiOptions>? configure = null,
        DateTimeOffset? now = null,
        Func<PageDigest>? digestFactory = null)
    {
        Mode = mode;
        User = Db.AddUser("pipeline-user");
        FakeLlm = llm;
        Llm = llm is null ? new NotConfiguredLlmClient() : llm;
        Clock = new FixedTimeProvider(now ?? new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        Ai = new AiOptions { FastModel = "test-fast", QualityModel = "test-quality", RetryBackoffMs = 0 };
        configure?.Invoke(Ai);
        Extraction = digestFactory is not null
            ? new FakeExtractionService(digestFactory)
            : new FakeExtractionService(digest ?? Digests.Simple());
    }

    // Builds a generator on a fresh database context (like one request would).
    public PipelineScanGenerator CreateGenerator(ILlmClient client, Guid? userId = null) =>
        CreateGenerator(userId, client);

    // Same, but with a given client instead of the harness default.
    public PipelineScanGenerator CreateGenerator(Guid? userId = null, ILlmClient? client = null)
    {
        var db = Db.CreateContext();
        var options = Options.Create(Ai);
        return new PipelineScanGenerator(
            Extraction,
            new FakeFingerprinter(
                new ScanTech { Name = "Next.js", Highlighted = true },
                new ScanTech { Name = "Vercel", Highlighted = true },
                new ScanTech { Name = "Tailwind CSS", Highlighted = false }),
            new DbExtractionCache(db, options, Clock),
            new LlmBudgetGuard(db, options, Clock),
            client ?? Llm,
            new PromptBuilder(),
            new FakeOwner(userId ?? User.Id),
            Options.Create(new ScannerModeOptions { Mode = Mode.ToString() }),
            options,
            Clock,
            NullLogger<PipelineScanGenerator>.Instance);
    }

    // Runs one scan of a fixed public URL.
    public Task<GeneratedScan> ScanAsync(Guid? userId = null) =>
        CreateGenerator(userId).GenerateAsync(new Uri("https://acme.example/"), CancellationToken.None);

    // All ledger rows, oldest first.
    public List<LlmUsageRecord> UsageRows()
    {
        using var db = Db.CreateContext();
        return db.LlmUsage.OrderBy(u => u.CreatedAt).ToList();
    }

    // Closes the database.
    public void Dispose() => Db.Dispose();
}
