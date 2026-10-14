using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;
using ProjectMD.Api.Tests.Scans;

namespace ProjectMD.Api.Tests.Ai;

// The real API host with Scanner:Mode and Ai settings under test control (the same host as the
// scans tests: SQLite, test tokens, real controller).
public sealed class ScannerModeApiFactory : ScansApiFactory
{
    private readonly Dictionary<string, string> _settings;

    // A host with the given extra configuration values and an optional hook to replace services.
    public ScannerModeApiFactory(Dictionary<string, string> settings, Action<IServiceCollection>? configure = null)
        : base(1000, 1000, null, configure)
    {
        _settings = settings;
    }

    // Applies the settings, then the normal test host setup.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        base.ConfigureWebHost(builder);
    }
}

// End-to-end tests of the new error mapping and of the pipeline running inside the real host.
public class AiApiTests
{
    // A generator that throws whatever it is told to.
    private sealed class ThrowingGenerator : IScanGenerator
    {
        private readonly Exception _exception;

        // Remembers the exception to throw.
        public ThrowingGenerator(Exception exception)
        {
            _exception = exception;
        }

        // Always throws.
        public Task<GeneratedScan> GenerateAsync(Uri url, CancellationToken ct) => throw _exception;
    }

    private static string NewUid(string name) => $"{name}-{Guid.NewGuid():N}";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> PostScanAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/scans", new { url = "example.com" });

    // A host whose generator throws the given exception.
    private static ScansApiFactory FactoryThrowing(Exception exception) =>
        new(1000, 1000, configure: services =>
        {
            services.RemoveAll<IScanGenerator>();
            services.AddSingleton<IScanGenerator>(new ThrowingGenerator(exception));
        });

    // An extraction failure about the address itself is a 400 with reason "extract" and its safe message.
    [Theory]
    [InlineData(ExtractionFailure.BlockedAddress)]
    [InlineData(ExtractionFailure.NotHtml)]
    public async Task Client_side_extraction_failures_are_400(ExtractionFailure failure)
    {
        using var factory = FactoryThrowing(new ExtractionException(failure, "That address is not a public web page."));
        var uid = NewUid("alice");

        var response = await PostScanAsync(factory.CreateClientFor(uid));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("extract", body.GetProperty("reason").GetString());
        Assert.Equal("That address is not a public web page.", body.GetProperty("detail").GetString());
        Assert.Equal(0, await factory.CountAllScansAsync(uid));
    }

    // A site or renderer problem is a 502 with reason "extract", and nothing internal in the body.
    [Theory]
    [InlineData(ExtractionFailure.Unreachable)]
    [InlineData(ExtractionFailure.TimedOut)]
    [InlineData(ExtractionFailure.TooLarge)]
    [InlineData(ExtractionFailure.RendererMissing)]
    [InlineData(ExtractionFailure.HttpError)]
    [InlineData(ExtractionFailure.Busy)]
    public async Task Server_side_extraction_failures_are_502(ExtractionFailure failure)
    {
        using var factory = FactoryThrowing(new ExtractionException(
            failure,
            "The page could not be reached.",
            new InvalidOperationException("secret internal detail 10.0.0.5")));
        var uid = NewUid("alice");

        var response = await PostScanAsync(factory.CreateClientFor(uid));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"reason\":\"extract\"", text);
        Assert.DoesNotContain("secret internal detail", text);
        Assert.DoesNotContain("10.0.0.5", text);
        Assert.Equal(0, await factory.CountAllScansAsync(uid));
    }

    // A budget stop that reaches the controller is a 429 with reason "budget", and the monthly
    // allowance is not used.
    [Theory]
    [InlineData(BudgetScope.User)]
    [InlineData(BudgetScope.Global)]
    public async Task Budget_stop_is_429_with_reason_budget(BudgetScope scope)
    {
        using var factory = FactoryThrowing(new BudgetExceededException(scope));
        var uid = NewUid("alice");
        var client = factory.CreateClientFor(uid);

        var response = await PostScanAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("budget", body.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.True(body.TryGetProperty("resetsAt", out _));
        Assert.Equal(0, await factory.CountAllScansAsync(uid));

        var me = await JsonAsync(await client.GetAsync("/api/me"));
        Assert.Equal(0, me.GetProperty("usage").GetProperty("used").GetInt32());
    }

    // Registers the fakes used by the pipeline end-to-end tests.
    private static Action<IServiceCollection> Fakes(ILlmClient? llm, Func<PageDigest>? digest = null) => services =>
    {
        services.RemoveAll<IExtractionService>();
        services.AddSingleton<IExtractionService>(new FakeExtractionService(digest ?? (() => Digests.Simple())));
        services.RemoveAll<ITechFingerprinter>();
        services.AddSingleton<ITechFingerprinter>(new FakeFingerprinter(new ScanTech { Name = "Next.js", Highlighted = true }));
        if (llm is not null)
        {
            services.RemoveAll<ILlmClient>();
            services.AddSingleton(llm);
        }
    };

    // The reason the pipeline test hosts exist: `extract` mode runs the real controller and pipeline,
    // saves a real document and never needs an AI client.
    [Fact]
    public async Task Extract_mode_creates_a_scan_from_the_pipeline()
    {
        using var factory = new ScannerModeApiFactory(new() { ["Scanner:Mode"] = "extract" }, Fakes(llm: null));
        var uid = NewUid("alice");

        var response = await PostScanAsync(factory.CreateClientFor(uid));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scan = (await response.Content.ReadFromJsonAsync<ScanDto>())!;
        Assert.Equal(1, scan.Pages);
        Assert.Contains("# example.com Documentation", scan.Markdown);
        Assert.Contains(DeterministicMarkdownBuilder.FooterExtract, scan.Markdown);
        Assert.Contains(scan.Tech, t => t.Name == "Next.js");
    }

    // ai mode with no AI client configured starts fine and behaves like extract with the footer.
    [Fact]
    public async Task Ai_mode_without_a_client_starts_and_falls_back()
    {
        using var factory = new ScannerModeApiFactory(new() { ["Scanner:Mode"] = "ai" }, Fakes(llm: null));

        var response = await PostScanAsync(factory.CreateClientFor(NewUid("alice")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scan = (await response.Content.ReadFromJsonAsync<ScanDto>())!;
        Assert.Contains("AI writing is not configured", scan.Markdown);
    }

    // ai mode with a (fake) client: one call, prose in the saved scan, usage charged to the caller.
    [Fact]
    public async Task Ai_mode_with_a_client_writes_prose_and_records_usage()
    {
        var llm = FakeLlmClient.Answering("## Summary\nAcme runs apps.", prompt: 700, completion: 90);
        using var factory = new ScannerModeApiFactory(
            new() { ["Scanner:Mode"] = "ai", ["Ai:FastModel"] = "test-fast" },
            Fakes(llm));
        var uid = NewUid("alice");

        var response = await PostScanAsync(factory.CreateClientFor(uid));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scan = (await response.Content.ReadFromJsonAsync<ScanDto>())!;
        Assert.Contains("Acme runs apps.", scan.Markdown);
        Assert.Equal(1, llm.Calls);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.LlmUsage.AsNoTracking().SingleAsync();
        Assert.Equal(700, row.PromptTokens);
        Assert.Equal(90, row.CompletionTokens);
        var owner = await db.AppUsers.AsNoTracking().SingleAsync(u => u.FirebaseUid == uid);
        Assert.Equal(owner.Id, row.UserId);
    }

    // With the fallback off, a daily cap that is already used up returns 429 "budget" end to end.
    [Fact]
    public async Task Ai_mode_over_the_cap_without_fallback_is_429()
    {
        var llm = FakeLlmClient.Answering();
        using var factory = new ScannerModeApiFactory(
            new()
            {
                ["Scanner:Mode"] = "ai",
                ["Ai:FastModel"] = "test-fast",
                ["Ai:DailyTokenCapPerUser"] = "100",
                ["Ai:FallbackOnBudget"] = "false",
            },
            Fakes(llm));

        var response = await PostScanAsync(factory.CreateClientFor(NewUid("alice")));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("budget", (await JsonAsync(response)).GetProperty("reason").GetString());
        Assert.Equal(0, llm.Calls);
    }

    // A real AI client without a model name stops the app at startup with a clear message.
    [Fact]
    public void Ai_mode_with_a_client_but_no_model_fails_at_startup()
    {
        using var factory = new ScannerModeApiFactory(new() { ["Scanner:Mode"] = "ai" }, Fakes(FakeLlmClient.Answering()));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Ai:FastModel", error.ToString());
    }

    // static mode (the default) is untouched: the mock generator and no AI tables are used.
    [Fact]
    public async Task Static_mode_still_uses_the_mock()
    {
        using var factory = new ScansApiFactory(1000, 1000);

        var response = await PostScanAsync(factory.CreateClientFor(NewUid("alice")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scan = (await response.Content.ReadFromJsonAsync<ScanDto>())!;
        Assert.Equal(StaticScanGenerator.MockDurationMs, scan.DurationMs);
    }
}
