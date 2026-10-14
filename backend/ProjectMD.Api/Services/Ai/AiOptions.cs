using Microsoft.Extensions.Options;

namespace ProjectMD.Api.Services.Ai;

// Settings for the AI half of the scan pipeline, bound from the "Ai" section of configuration.
// Every default is chosen to spend as few credits as possible. Model names are deliberately empty
// by default: they are never hard-coded, and they are only required once a real AI client exists.
public sealed class AiOptions
{
    // The longest input (system prompt plus page content) we ever send, in estimated tokens
    // (Ai:MaxInputTokens, default 3000). PromptBuilder truncates by priority to stay under it.
    public int MaxInputTokens { get; set; } = 3000;

    // Hard cap on the answer length, sent as the provider's max_tokens (Ai:MaxOutputTokens, default 700).
    public int MaxOutputTokens { get; set; } = 700;

    // The cheap model used for everything (Ai:FastModel). Empty until configured.
    public string FastModel { get; set; } = string.Empty;

    // The better, dearer model, used only if a future option asks for it (Ai:QualityModel). Empty
    // until configured.
    public string QualityModel { get; set; } = string.Empty;

    // How many days a cached answer is reused (Ai:CacheDays, default 7). 0 turns the cache off.
    public int CacheDays { get; set; } = 7;

    // Most tokens (input plus output) one user may spend per UTC day (Ai:DailyTokenCapPerUser, default 20000).
    public int DailyTokenCapPerUser { get; set; } = 20_000;

    // Most tokens all users together may spend per UTC day (Ai:DailyTokenCapGlobal, default 200000).
    public int DailyTokenCapGlobal { get; set; } = 200_000;

    // When a daily cap is hit: true (default) finishes the scan with the basic, AI-free document and
    // shows no error; false answers 429 with reason "budget" instead (Ai:FallbackOnBudget).
    public bool FallbackOnBudget { get; set; } = true;

    // Pause before the single retry after a provider 429/5xx, in milliseconds (Ai:RetryBackoffMs,
    // default 1000). There is never more than one retry.
    public int RetryBackoffMs { get; set; } = 1000;
}

// Checks the Ai settings once at startup (in `ai` mode) so a bad value stops the app with one
// clear message instead of failing a scan later.
public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    // The longest model name the cache and usage tables can hold.
    public const int MaxModelNameLength = 100;

    private readonly ILlmClient _client;

    // Receives the registered AI client, because model names are only required for a real one.
    public AiOptionsValidator(ILlmClient client)
    {
        _client = client;
    }

    // Returns Success, or Fail with every problem listed. Model names are required only when the
    // registered client is a real one (not NotConfiguredLlmClient); otherwise `ai` mode behaves
    // like `extract` and never needs them.
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        var errors = new List<string>();

        // The smallest input budget that still fits the fixed instructions plus some page content.
        var minInput = PromptBuilder.MinimumInputTokens;
        if (options.MaxInputTokens < minInput)
        {
            errors.Add($"Ai:MaxInputTokens must be at least {minInput} (the fixed instructions use part of it).");
        }

        if (options.MaxOutputTokens <= 0)
        {
            errors.Add("Ai:MaxOutputTokens must be greater than 0.");
        }

        if (options.CacheDays < 0)
        {
            errors.Add("Ai:CacheDays must be 0 (cache off) or more.");
        }

        if (options.DailyTokenCapPerUser <= 0)
        {
            errors.Add("Ai:DailyTokenCapPerUser must be greater than 0.");
        }

        if (options.DailyTokenCapGlobal <= 0)
        {
            errors.Add("Ai:DailyTokenCapGlobal must be greater than 0.");
        }

        if (options.RetryBackoffMs is < 0 or > 10_000)
        {
            errors.Add("Ai:RetryBackoffMs must be between 0 and 10000.");
        }

        if (options.FastModel.Length > MaxModelNameLength || options.QualityModel.Length > MaxModelNameLength)
        {
            errors.Add($"Ai model names must be at most {MaxModelNameLength} characters.");
        }

        if (_client is not NotConfiguredLlmClient && string.IsNullOrWhiteSpace(options.FastModel))
        {
            errors.Add("Ai:FastModel is required when AI mode is on and an AI client is configured. Set it as configuration (for example the environment variable Ai__FastModel).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
