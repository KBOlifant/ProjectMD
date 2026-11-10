using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;
using ProjectMD.Api.Tests.Ai;
using ProjectMD.Api.Services.Groq;

namespace ProjectMD.Api.Tests.Scans;

// A [Fact] that is skipped unless PROJECTMD_LIVE_SCAN is "1". It scans a real public site, so it
// needs a browser and (for the AI part) a Groq key in the environment.
public sealed class LiveScanFactAttribute : FactAttribute
{
    public LiveScanFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROJECTMD_LIVE_SCAN") != "1")
        {
            Skip = "Set PROJECTMD_LIVE_SCAN=1 to scan a real public site (see backend/README.md).";
        }
    }
}

// Opt-in live check: runs the real pipeline in this process (no server, no real database) against a
// public site and writes the Markdown and the analysis JSON to a folder you choose.
//   PROJECTMD_LIVE_URL  site to scan (default https://tailwindcss.com)
//   PROJECTMD_LIVE_OUT  folder for scan.md and analysis.json (default: the temp folder)
//   Ai__ApiKey, Ai__FastModel  read from the environment only; the key is never printed or written
//   Scanner__BrowserChannel    "chrome" or "msedge" to use an installed browser
[Collection("Playwright")]
public class LiveScanTests
{
    // Scans the site and writes the results.
    [LiveScanFact]
    public async Task Live_scan_of_public_site_writes_analysis()
    {
        var url = Environment.GetEnvironmentVariable("PROJECTMD_LIVE_URL");
        var target = new Uri(string.IsNullOrWhiteSpace(url) ? "https://tailwindcss.com" : url);
        var outDir = Environment.GetEnvironmentVariable("PROJECTMD_LIVE_OUT");
        outDir = string.IsNullOrWhiteSpace(outDir) ? Path.Combine(Path.GetTempPath(), "projectmd-live") : outDir;

        var apiKey = Environment.GetEnvironmentVariable("Ai__ApiKey") ?? string.Empty;
        var settings = new Dictionary<string, string?>
        {
            ["Scanner:Mode"] = string.IsNullOrWhiteSpace(apiKey) ? "extract" : "ai",
            ["Scanner:BrowserChannel"] = Environment.GetEnvironmentVariable("Scanner__BrowserChannel") ?? string.Empty,
            ["Ai:ApiKey"] = apiKey,
            ["Ai:FastModel"] = Environment.GetEnvironmentVariable("Ai__FastModel") ?? string.Empty,
        };
        if (Environment.GetEnvironmentVariable("PROJECTMD_LIVE_MOTION") == "1")
        {
            // Screenshots, animation evidence and the vision call, with the values from appsettings.json.
            settings["Motion:Enabled"] = "true";
            settings["Motion:AiEnabled"] = "true";
            settings["Motion:VisionModel"] = "qwen/qwen3.8-27b";
            settings["Motion:ResponseFormat"] = "json_object";
            settings["Motion:ReasoningEffort"] = "none";
            settings["Motion:MaxOutputTokens"] = "1800";
            settings["Motion:TimeoutSeconds"] = "60";
            settings["Ai:DailyTokenCapPerUser"] = "60000";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        using var db = new TestDatabase();
        var user = db.AddUser("live-user");

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.Configure<ScannerModeOptions>(configuration.GetSection("Scanner"));
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.AddSingleton<ProjectMD.Api.Services.IUrlValidator, ProjectMD.Api.Services.UrlValidator>();
        services.AddExtraction(configuration);
        services.AddHttpClient(GroqLlmClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddSingleton<ILlmClient, NotConfiguredLlmClient>();
        }
        else
        {
            services.AddSingleton<ILlmClient, GroqLlmClient>();
        }

        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<MotionPromptBuilder>();
        services.AddScoped(_ => db.CreateContext());
        services.AddScoped<IExtractionCache, DbExtractionCache>();
        services.AddScoped<ILlmBudgetGuard, LlmBudgetGuard>();
        services.AddScoped<IScanOwnerProvider>(_ => new FakeOwner(user.Id));
        services.AddScoped<IMotionAnalyzer, MotionAnalyzer>();
        services.AddScoped<IComponentAnalyzer, ComponentAnalyzer>();
        services.AddScoped<IScanGenerator, PipelineScanGenerator>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var generator = scope.ServiceProvider.GetRequiredService<IScanGenerator>();

        var scan = await generator.GenerateAsync(target, CancellationToken.None);

        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "scan.md"), scan.Markdown);
        Assert.NotNull(scan.Analysis);
        await File.WriteAllTextAsync(Path.Combine(outDir, "analysis.json"), ScanAnalysisJson.Serialize(scan.Analysis!));
        Assert.NotEmpty(scan.Markdown);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.DoesNotContain(apiKey, scan.Markdown);
            Assert.DoesNotContain(apiKey, ScanAnalysisJson.Serialize(scan.Analysis!));
        }
    }
}
