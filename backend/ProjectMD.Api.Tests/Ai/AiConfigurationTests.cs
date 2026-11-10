using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;
using ProjectMD.Api.Services.Groq;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the AI settings (defaults and validation) and for how Scanner:Mode picks the generator.
public class AiConfigurationTests
{
    // Registers the scan services the way Program.cs does, from in-memory settings. Nothing is
    // started: the tests look at the registrations and at plain options objects.
    private static ServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScanServices(configuration);
        return services;
    }

    // The documented defaults.
    [Fact]
    public void Ai_defaults_are_the_cheap_ones()
    {
        using var provider = Register([]).BuildServiceProvider();

        var ai = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        Assert.Equal(3000, ai.MaxInputTokens);
        Assert.Equal(800, ai.MaxOutputTokens);
        Assert.Equal(7, ai.CacheDays);
        Assert.Equal(20_000, ai.DailyTokenCapPerUser);
        Assert.Equal(200_000, ai.DailyTokenCapGlobal);
        Assert.True(ai.FallbackOnBudget);
        Assert.Equal(1000, ai.RetryBackoffMs);
        Assert.Equal(string.Empty, ai.FastModel);
        Assert.Equal(string.Empty, ai.QualityModel);
    }

    // Values come from the "Ai" section.
    [Fact]
    public void Ai_settings_are_read_from_configuration()
    {
        using var provider = Register(new()
        {
            ["Ai:MaxInputTokens"] = "2000",
            ["Ai:MaxOutputTokens"] = "400",
            ["Ai:FastModel"] = "some-fast-model",
            ["Ai:CacheDays"] = "3",
            ["Ai:DailyTokenCapPerUser"] = "9000",
            ["Ai:DailyTokenCapGlobal"] = "90000",
            ["Ai:FallbackOnBudget"] = "false",
        }).BuildServiceProvider();

        var ai = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        Assert.Equal(2000, ai.MaxInputTokens);
        Assert.Equal(400, ai.MaxOutputTokens);
        Assert.Equal("some-fast-model", ai.FastModel);
        Assert.Equal(3, ai.CacheDays);
        Assert.Equal(9000, ai.DailyTokenCapPerUser);
        Assert.Equal(90_000, ai.DailyTokenCapGlobal);
        Assert.False(ai.FallbackOnBudget);
    }

    // The default settings are valid with no real client (no model name needed).
    [Fact]
    public void Defaults_are_valid_while_the_client_is_not_configured()
    {
        var result = new AiOptionsValidator(new NotConfiguredLlmClient()).Validate(null, new AiOptions());

        Assert.True(result.Succeeded);
    }

    // A real client needs a model name; the message says which setting.
    [Fact]
    public void A_real_client_requires_a_fast_model_name()
    {
        var validator = new AiOptionsValidator(FakeLlmClient.Answering());

        var result = validator.Validate(null, new AiOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ai:FastModel"));
        Assert.True(validator.Validate(null, new AiOptions { FastModel = "m" }).Succeeded);
    }

    // Caps and budgets must be positive, and the input budget must leave room for the instructions.
    [Theory]
    [InlineData("MaxInputTokens", 50)]
    [InlineData("MaxOutputTokens", 0)]
    [InlineData("DailyTokenCapPerUser", 0)]
    [InlineData("DailyTokenCapGlobal", -1)]
    [InlineData("CacheDays", -1)]
    [InlineData("RetryBackoffMs", 60_000)]
    public void Bad_numbers_are_rejected(string name, int value)
    {
        var options = new AiOptions();
        typeof(AiOptions).GetProperty(name)!.SetValue(options, value);

        var result = new AiOptionsValidator(new NotConfiguredLlmClient()).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ai:" + name));
    }

    // Static is the default mode and keeps the MOCK generator; the extraction code is not registered.
    [Fact]
    public void Static_mode_keeps_the_mock_generator()
    {
        var services = Register([]);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.IsType<StaticScanGenerator>(scope.ServiceProvider.GetRequiredService<IScanGenerator>());
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IExtractionService));
    }

    // Extract and ai modes use the pipeline generator and register the extraction code.
    [Theory]
    [InlineData("extract")]
    [InlineData("ai")]
    [InlineData("AI")]
    public void Extract_and_ai_modes_use_the_pipeline_generator(string mode)
    {
        var services = Register(new() { ["Scanner:Mode"] = mode });

        var generator = Assert.Single(services, d => d.ServiceType == typeof(IScanGenerator));
        Assert.Equal(typeof(PipelineScanGenerator), generator.ImplementationType);
        Assert.Contains(services, d => d.ServiceType == typeof(IExtractionService));
        Assert.Contains(services, d => d.ServiceType == typeof(ITechFingerprinter));
    }

    // An unknown mode stops startup with a clear message.
    [Fact]
    public void Unknown_mode_is_a_clear_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(new() { ["Scanner:Mode"] = "turbo" }));

        Assert.Contains("Scanner:Mode", error.Message);
    }

    // Only the "not configured" client is registered, so ai mode needs no model name: startup
    // validation passes and the one-time notice is registered.
    [Fact]
    public void Ai_mode_with_the_not_configured_client_passes_startup_validation()
    {
        var services = Register(new() { ["Scanner:Mode"] = "ai" });
        using var provider = services.BuildServiceProvider();

        Assert.IsType<NotConfiguredLlmClient>(provider.GetRequiredService<ILlmClient>());
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AiModeStartupNotice));
    }

    // Bad Ai settings stop `ai` mode at startup, but are ignored while the AI is not in use.
    [Fact]
    public void Bad_ai_settings_fail_startup_only_in_ai_mode()
    {
        using var ai = Register(new() { ["Scanner:Mode"] = "ai", ["Ai:DailyTokenCapPerUser"] = "0" }).BuildServiceProvider();
        var error = Assert.ThrowsAny<Exception>(() => ai.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("Ai:DailyTokenCapPerUser", error.ToString());

        using var extract = Register(new() { ["Scanner:Mode"] = "extract", ["Ai:DailyTokenCapPerUser"] = "0" }).BuildServiceProvider();
        extract.GetRequiredService<IStartupValidator>().Validate();
    }

    // The notice logs once at startup in ai mode and never throws.
    [Fact]
    public async Task Startup_notice_runs_without_error()
    {
        var notice = new AiModeStartupNotice(new NotConfiguredLlmClient(), NullLogger<AiModeStartupNotice>.Instance);

        await notice.StartAsync(CancellationToken.None);
        await notice.StopAsync(CancellationToken.None);
    }

    // The NotConfigured client never makes a call: it always throws the dedicated exception.
    [Fact]
    public async Task Not_configured_client_always_throws()
    {
        await Assert.ThrowsAsync<LlmNotConfiguredException>(() =>
            new NotConfiguredLlmClient().CompleteAsync(new LlmRequest("s", "u", "m", 10), CancellationToken.None));
    }
}
