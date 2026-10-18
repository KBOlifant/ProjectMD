using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Services;

// One call that registers everything the scans API needs. Program.cs calls it from the SCAN
// SERVICES block.
public static class ScanServiceCollectionExtensions
{
    // Registers the clock, the URL validator, the scan generator chosen by Scanner:Mode, the AI
    // pieces (settings, prompt builder, cache, budget guard, LLM seam), the usage counter, the
    // quota-checked scan recorder, the settings and the per-user rate limiter.
    //
    // Scanner:Mode picks the IScanGenerator:
    //   static  (default)  the MOCK StaticScanGenerator. Nothing else changes.
    //   extract            PipelineScanGenerator: real fetch + reduce + fingerprint, no AI.
    //   ai                 PipelineScanGenerator plus the AI stage (falls back to extract output
    //                      when no AI client is configured, which is the case today).
    // An unknown mode throws at startup with a clear message.
    public static IServiceCollection AddScanServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<UsageOptions>(configuration.GetSection("Usage"));
        services.Configure<ScanRateLimitOptions>(configuration.GetSection("RateLimiting:ScanCreate"));
        services.Configure<ScannerModeOptions>(configuration.GetSection("Scanner"));

        services.AddSingleton<IUrlValidator, UrlValidator>();

        var mode = ScannerModeOptions.Parse(configuration["Scanner:Mode"]);
        services.AddAiPipeline(configuration, mode);

        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<IScanRecorder, ScanRecorder>();

        services.AddScanRateLimiting();
        return services;
    }

    // Registers the AI settings and helpers (always, they cost nothing until used) and picks the
    // generator for the mode. The extraction code (Playwright and friends) is only registered when
    // the mode needs it, so `static` mode never touches it.
    private static void AddAiPipeline(this IServiceCollection services, IConfiguration configuration, ScannerMode mode)
    {
        // Settings. They are validated at startup only in `ai` mode, so a bad Ai value can never
        // stop the app while the AI is not in use.
        var aiOptions = services.AddOptions<AiOptions>().Bind(configuration.GetSection("Ai"));
        services.AddSingleton<IValidateOptions<AiOptions>>(
            sp => new AiOptionsValidator(() => sp.GetRequiredService<ILlmClient>()));
        if (mode == ScannerMode.Ai)
        {
            aiOptions.ValidateOnStart();
            services.AddHostedService<AiModeStartupNotice>();
        }

        // The seam to the language model. The real Groq client is registered only in `ai` mode when
        // Ai:ApiKey is non-empty; otherwise the "not configured" client stays. A real client is
        // registered before the TryAdd line below, so it wins.
        if (mode == ScannerMode.Ai && !string.IsNullOrWhiteSpace(configuration["Ai:ApiKey"]))
        {
            // No automatic redirects (a redirect must never carry the bearer key elsewhere) and no
            // built-in request logging; the client logs its own safe summary.
            services.AddHttpClient(GroqLlmClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
                .RemoveAllLoggers();
            services.AddSingleton<ILlmClient, GroqLlmClient>();
        }

        services.TryAddSingleton<ILlmClient, NotConfiguredLlmClient>();

        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<MotionPromptBuilder>();
        services.AddScoped<IMotionAnalyzer, MotionAnalyzer>();        services.AddScoped<IExtractionCache, DbExtractionCache>();
        services.AddScoped<ILlmBudgetGuard, LlmBudgetGuard>();
        services.AddScoped<IScanOwnerProvider, CurrentScanOwnerProvider>();

        if (mode == ScannerMode.Static)
        {
            // MOCK generator, exactly as before.
            services.AddSingleton<IScanGenerator, StaticScanGenerator>();
            return;
        }

        services.AddExtraction(configuration);
        services.AddScoped<IScanGenerator, PipelineScanGenerator>();
    }
}
