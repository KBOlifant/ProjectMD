namespace ProjectMD.Api.Services.Ai;

// One question for the language model: fixed instructions (System), the page facts (User), the
// model name and the hard cap on the answer length. JsonSchema, when set, asks the provider for JSON
// that follows that schema (structured output); null keeps the plain-text behaviour.
// The rest are for vision requests and keep the old behaviour when left out: Images are sent
// with the user message (the model must accept images); ReasoningEffort, when set, replaces
// Ai:ReasoningEffort for this request (for example "none" for a vision model); SchemaName is the
// name given to the JSON schema; JsonObjectMode asks for any valid JSON object (response_format
// json_object) instead of strict schema decoding, for models or inputs where strict mode is
// refused (the prompt must then describe the JSON itself); TimeoutSeconds, when set, replaces
// Ai:TimeoutSeconds for this request.
public sealed record LlmRequest(
    string System,
    string User,
    string Model,
    int MaxOutputTokens,
    string? JsonSchema = null,
    IReadOnlyList<LlmImage>? Images = null,
    string? ReasoningEffort = null,
    string SchemaName = "scan_notes",
    bool JsonObjectMode = false,
    int? TimeoutSeconds = null);

// One image sent to a vision model: its media type (for example "image/jpeg") and its bytes. The
// bytes are never logged.
public sealed record LlmImage(string MediaType, byte[] Data);

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

    // True for a client that allows extra output tokens for hidden reasoning (see
    // AiOptions.ReasoningTokenHeadroom). The budget guard then reserves the same larger total.
    bool AddsReasoningHeadroom => false;
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

