using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProjectMD.Api.Services;

// One call that registers everything the scans API needs. Program.cs calls it from the SCAN
// SERVICES block.
public static class ScanServiceCollectionExtensions
{
    // Registers the clock, the URL validator, the MOCK scan generator, the usage counter, the
    // quota-checked scan recorder, the settings and the per-user rate limiter.
    public static IServiceCollection AddScanServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<UsageOptions>(configuration.GetSection("Usage"));
        services.Configure<ScanRateLimitOptions>(configuration.GetSection("RateLimiting:ScanCreate"));

        services.AddSingleton<IUrlValidator, UrlValidator>();

        // MOCK generator today; a Groq-backed IScanGenerator replaces this registration later.
        services.AddSingleton<IScanGenerator, StaticScanGenerator>();

        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<IScanRecorder, ScanRecorder>();

        services.AddScanRateLimiting();
        return services;
    }
}
