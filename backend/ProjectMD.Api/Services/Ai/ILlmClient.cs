namespace ProjectMD.Api.Services.Ai;

// One question for the language model: fixed instructions (System), the page facts (User), the
// model name and the hard cap on the answer length.
public sealed record LlmRequest(string System, string User, string Model, int MaxOutputTokens);

// The model's answer plus the REAL token counts reported by the provider (recorded in the usage
// ledger), and the model that actually answered.
public sealed record LlmResult(string Text, int PromptTokens, int CompletionTokens, string Model);

// The seam to the language model. There is no real implementation yet: a future one will talk to
// the provider, must use temperature 0 (the request has no field for it, so it is the client's
// job) and must read its key from server-side configuration only. It should throw
// LlmProviderException for provider 429/5xx or network trouble and LlmNotConfiguredException when
// it has no key. Nothing else in the pipeline knows which provider is behind this.
public interface ILlmClient
{
    // Sends one request and returns the answer. Must honour request.MaxOutputTokens as a hard cap.
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}

// The only ILlmClient that exists today. It never makes a network call; it always says the AI is
// not configured, so `ai` mode quietly behaves like `extract` until a real client is added.
public sealed class NotConfiguredLlmClient : ILlmClient
{
    // Always throws LlmNotConfiguredException.
    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        throw new LlmNotConfiguredException();
}

// Thrown when AI writing was asked for but no AI client or key is configured.
public sealed class LlmNotConfiguredException : Exception
{
    // Creates the exception with a fixed, safe message.
    public LlmNotConfiguredException()
        : base("AI writing is not configured.")
    {
    }
}

// Thrown by a real client when the provider failed. The pipeline retries once when IsTransient
// is true (provider 429 or 5xx, timeouts) and never loops.
public sealed class LlmProviderException : Exception
{
    // The provider's HTTP status when there was one.
    public int? StatusCode { get; }

    // True for failures worth one more try: provider 429, 5xx, or a network/timeout error.
    public bool IsTransient { get; }

    // Creates the exception. The message must be safe to log (no keys, no page content).
    public LlmProviderException(string message, int? statusCode, bool isTransient, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
    }
}
