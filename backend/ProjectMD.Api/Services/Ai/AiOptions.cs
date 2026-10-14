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

    // Where the Groq (OpenAI-compatible) API lives (Ai:BaseUrl). The client adds /chat/completions.
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    // The provider key (Ai:ApiKey). Secret: set it only in backend/.env (Ai__ApiKey) or a real
    // environment variable, never in a tracked file. Empty means no real AI client is registered.
    public string ApiKey { get; set; } = string.Empty;

    // How hard a reasoning model thinks before answering (Ai:ReasoningEffort, default "low"): low,
    // medium or high. Hidden reasoning tokens are billed as output, so low saves credits.
    public string ReasoningEffort { get; set; } = "low";

    // Extra output tokens allowed for hidden reasoning on top of Ai:MaxOutputTokens
    // (Ai:ReasoningTokenHeadroom, default 400). Added by the Groq client AND reserved by the budget.
    public int ReasoningTokenHeadroom { get; set; } = 400;

    // Seconds to wait for the provider before giving up (Ai:TimeoutSeconds, default 30).
    public int TimeoutSeconds { get; set; } = 30;

    // The one place that decides the hard output cap: the answer allowance, plus the reasoning
    // headroom when the client is one that adds it. The client sends this number and the budget
    // guard reserves this same number, so the daily limits stay honest.
    public int OutputTokenCap(bool withReasoningHeadroom) =>
        MaxOutputTokens + (withReasoningHeadroom ? Math.Max(0, ReasoningTokenHeadroom) : 0);
}

// Checks the Ai settings once at startup (in `ai` mode) so a bad value stops the app with one
// clear message instead of failing a scan later.
public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    // The longest model name the cache and usage tables can hold.
    public const int MaxModelNameLength = 100;

    private readonly Func<ILlmClient> _clientSource;

    // Receives the AI client directly (used by tests).
    public AiOptionsValidator(ILlmClient client)
        : this(() => client)
    {
    }

    // Receives a way to get the registered AI client later, because model names are only required
    // for a real one. It is a function (not the client itself) so building the client, which reads
    // the options, can never loop back into this validator.
    public AiOptionsValidator(Func<ILlmClient> clientSource)
    {
        _clientSource = clientSource;
    }

    private ILlmClient _client => _clientSource();

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

        if (options.ReasoningTokenHeadroom is < 0 or > 20_000)
        {
            errors.Add("Ai:ReasoningTokenHeadroom must be between 0 and 20000.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Ai:TimeoutSeconds must be between 1 and 300.");
        }

        if (!string.Equals(options.ReasoningEffort, "low", StringComparison.Ordinal)
            && !string.Equals(options.ReasoningEffort, "medium", StringComparison.Ordinal)
            && !string.Equals(options.ReasoningEffort, "high", StringComparison.Ordinal))
        {
            errors.Add("Ai:ReasoningEffort must be low, medium or high.");
        }

        if (_client is GroqLlmClient
            && (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
                || (baseUrl.Scheme != Uri.UriSchemeHttps && !baseUrl.IsLoopback)))
        {
            errors.Add("Ai:BaseUrl must be an absolute https address.");
        }

        if (_client is not NotConfiguredLlmClient && string.IsNullOrWhiteSpace(options.FastModel))
        {
            errors.Add("Ai:FastModel is required when AI mode is on and an AI client is configured. Set it as configuration (for example the environment variable Ai__FastModel).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
