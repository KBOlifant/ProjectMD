using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using ProjectMD.Api.Services.Ai;

namespace ProjectMD.Api.Services.Groq;

// The real ILlmClient: one call to Groq's OpenAI-compatible chat completions endpoint, made with the
// official OpenAI .NET package (ChatClient pointed at Ai:BaseUrl). Groq-only request fields the
// package has no property for (include_reasoning, reasoning_format) are added with the options'
// Patch. Docs: https://console.groq.com/docs/openai, /structured-outputs and /vision.
//
// Credit rules: temperature 0, low reasoning effort by default, reasoning text switched off, a hard
// output cap of the answer allowance plus a small reasoning headroom, no streaming, and no retry
// except one for a 429/5xx. The package's own retries are switched off so this class stays the only
// place that retries. A cut-off or empty answer is returned as empty text WITH the real token
// counts, so the pipeline falls back to the basic document and still records what was spent.
//
// Secrecy: the key is only handed to the package's credential. It is never logged, never in an
// exception message, and neither are prompts or response bodies (the package's own exception text
// contains the response body, so it is never passed on); only status, model, token counts and
// latency are logged.
public sealed class GroqLlmClient : ILlmClient
{
    // Name of the HttpClient registered for this client (it carries the no-redirect handler).
    public const string HttpClientName = "groq";

    // The safe message shown (and logged) when Groq refuses the key.
    public const string RejectedCredentialsMessage = "The AI service rejected the server's credentials";

    // Longest Retry-After we will honour before the one retry.
    private const int MaxRetryAfterSeconds = 5;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<GroqLlmClient> _logger;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new(StringComparer.Ordinal);

    // Receives the HTTP client factory, the AI settings and a logger.
    public GroqLlmClient(IHttpClientFactory httpFactory, IOptions<AiOptions> options, ILogger<GroqLlmClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _logger = logger;
    }

    // This client allows reasoning headroom on top of the answer cap.
    public bool AddsReasoningHeadroom => true;

    // Sends the request (and at most one retry after a 429/5xx) and returns the answer with the
    // provider's real token counts. Throws LlmProviderException for every failure; the messages are
    // safe to log. Never retries a timeout, a 401/403 or any other 4xx.
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new LlmNotConfiguredException();
        }

        var chat = ChatClientFor(request.Model, options);
        var messages = BuildMessages(request);
        var chatOptions = BuildOptions(request, options);

        for (var attempt = 0; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            var timeoutSeconds = request.TimeoutSeconds is > 0 ? request.TimeoutSeconds.Value : options.TimeoutSeconds;
            var (completion, status, errorBody, retryAfter) = await SendOnceAsync(chat, messages, chatOptions, timeoutSeconds, ct);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (completion is not null)
            {
                return ToResult(completion, request.Model, elapsedMs);
            }

            if (status is 401 or 403)
            {
                _logger.LogWarning("AI provider rejected the credentials (status {Status}, model {Model})", status, request.Model);
                throw new LlmProviderException(RejectedCredentialsMessage, status, isTransient: false);
            }

            if (status == 429 || status >= 500)
            {
                if (attempt == 0)
                {
                    var delay = ChooseDelay(retryAfter, options.RetryBackoffMs);
                    _logger.LogWarning("AI provider returned status {Status}; retrying once after {DelayMs} ms", status, (long)delay.TotalMilliseconds);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct);
                    }

                    continue;
                }

                // Already retried once here, so tell the pipeline not to retry again.
                throw new LlmProviderException(
                    $"The AI service is busy or unavailable (status {status})", status, isTransient: false);
            }

            // Groq's error code, type and parameter name are fixed identifiers (for example
            // "invalid_request_error" / "response_format"), never page content or the prompt, so they
            // are logged to make a refused request diagnosable. The error message text is not.
            var (errorType, errorCode, errorParam) = ProviderErrorIds(errorBody);
            _logger.LogWarning(
                "AI provider refused the request (status {Status}, model {Model}, error type {ErrorType}, code {ErrorCode}, param {ErrorParam})",
                status, request.Model, errorType, errorCode, errorParam);
            throw new LlmProviderException($"The AI service rejected the request (status {status})", status, isTransient: false);
        }
    }

    // One ChatClient per model, built on the factory's HttpClient so the no-redirect handler (and the
    // tests' fake network) is used. The package's retries are off (see the class comment), and its
    // own network timeout is set above Ai:TimeoutSeconds' maximum so only our timeout ends a call.
    private ChatClient ChatClientFor(string model, AiOptions options) =>
        _chatClients.GetOrAdd(model, m => new ChatClient(
            m,
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
                Transport = new HttpClientPipelineTransport(_httpFactory.CreateClient(HttpClientName)),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                NetworkTimeout = TimeSpan.FromMinutes(6),
            }));

    // The system message, then the user message: plain text, or (with images) a text part followed
    // by one image part per image (sent as a base64 data URL, the shape Groq's vision docs show).
    internal static List<ChatMessage> BuildMessages(LlmRequest request)
    {
        ChatMessage user = request.Images is { Count: > 0 } images
            ? new UserChatMessage(
                [ChatMessageContentPart.CreateTextPart(request.User),
                 .. images.Select(i => ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(i.Data), i.MediaType))])
            : new UserChatMessage(request.User);

        return [new SystemChatMessage(request.System), user];
    }

    // The settings sent with the messages. Temperature is always 0; reasoning text is switched off;
    // the output cap is the request's cap plus the reasoning headroom (the same total the budget
    // reserves). A request with its own ReasoningEffort uses it and asks for hidden reasoning with
    // reasoning_format (the include_reasoning flag is a gpt-oss setting).
#pragma warning disable SCME0001 // Patch: the package's way to send fields it has no property for
    internal static ChatCompletionOptions BuildOptions(LlmRequest request, AiOptions options)
    {
        var chat = new ChatCompletionOptions
        {
            Temperature = 0,
            MaxOutputTokenCount = request.MaxOutputTokens + Math.Max(0, options.ReasoningTokenHeadroom),
        };

        if (request.ReasoningEffort is { Length: > 0 } effort)
        {
            chat.Patch.Set("$.reasoning_effort"u8, effort);
            if (!string.Equals(effort, "none", StringComparison.Ordinal))
            {
                chat.Patch.Set("$.reasoning_format"u8, "hidden");
            }
        }
        else
        {
            chat.Patch.Set("$.reasoning_effort"u8, options.ReasoningEffort);
            chat.Patch.Set("$.include_reasoning"u8, false);
        }

        if (request.JsonObjectMode)
        {
            // Any valid JSON object; the prompt itself describes the shape. Groq's vision docs show
            // this mode working together with image input.
            chat.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }
        else if (request.JsonSchema is not null)
        {
            // Strict structured output: constrained decoding makes the answer match the schema.
            chat.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                request.SchemaName, BinaryData.FromString(request.JsonSchema), jsonSchemaIsStrict: true);
        }

        return chat;
    }
#pragma warning restore SCME0001

    // One call with the timeout applied. Returns the completion on success; on an HTTP error the
    // status, the error body (read only for its fixed identifiers) and Retry-After. Throws
    // LlmProviderException (not transient) for a timeout or a network failure.
    private async Task<(ChatCompletion? Completion, int Status, string? ErrorBody, TimeSpan? RetryAfter)> SendOnceAsync(
        ChatClient chat, List<ChatMessage> messages, ChatCompletionOptions chatOptions, int timeoutSeconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        try
        {
            ChatCompletion completion = await chat.CompleteChatAsync(messages, chatOptions, timeout.Token);
            return (completion, 200, null, null);
        }
        catch (ClientResultException ex) when (ex.Status != 0)
        {
            var response = ex.GetRawResponse();
            return (null, ex.Status, response?.Content.ToString(), ReadRetryAfter(response));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new LlmProviderException("The AI service returned an unreadable answer", 200, isTransient: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI provider call timed out after {Seconds} s", timeoutSeconds);
            throw new LlmProviderException("The AI service did not answer in time", null, isTransient: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or ClientResultException && !ct.IsCancellationRequested)
        {
            // Only the exception type is logged: nothing a proxy or provider echoes back.
            _logger.LogWarning("AI provider could not be reached ({ErrorType})", ex.GetType().Name);
            throw new LlmProviderException("The AI service could not be reached", null, isTransient: false);
        }
    }

    // Turns a completion into an LlmResult. A cut-off (finish_reason "length") or empty answer comes
    // back with EMPTY text and the real token counts, so it counts as a failed call but is recorded.
    private LlmResult ToResult(ChatCompletion completion, string model, long elapsedMs)
    {
        var content = completion.Content.Count > 0 ? completion.Content[0].Text ?? string.Empty : string.Empty;
        var promptTokens = Math.Max(0, completion.Usage?.InputTokenCount ?? 0);
        var completionTokens = Math.Max(0, completion.Usage?.OutputTokenCount ?? 0);
        var finish = completion.FinishReason.ToString();

        _logger.LogInformation(
            "AI call finished: status {Status}, model {Model}, prompt tokens {PromptTokens}, completion tokens {CompletionTokens}, finish {Finish}, {ElapsedMs} ms",
            200, model, promptTokens, completionTokens, finish, elapsedMs);

        if (completion.FinishReason == ChatFinishReason.Length || string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("AI answer was cut off or empty (finish {Finish}); using the basic document", finish);
            return new LlmResult(string.Empty, promptTokens, completionTokens, model);
        }

        return new LlmResult(content, promptTokens, completionTokens, model);
    }

    // The Retry-After header as a delay in seconds, or null when absent or not a number.
    private static TimeSpan? ReadRetryAfter(PipelineResponse? response) =>
        response is not null
        && response.Headers.TryGetValue("Retry-After", out var value)
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    // Groq's error type, code and param from an error body, each kept only when it looks like an
    // identifier (letters, digits, '_', '.', '-', at most 60 characters); "-" otherwise. Never the
    // message text.
    internal static (string Type, string Code, string Param) ProviderErrorIds(string? json)
    {
        const string none = "-";
        if (string.IsNullOrWhiteSpace(json))
        {
            return (none, none, none);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return (none, none, none);
            }

            return (Identifier(error, "type") ?? none, Identifier(error, "code") ?? none, Identifier(error, "param") ?? none);
        }
        catch (JsonException)
        {
            return (none, none, none);
        }
    }

    // One identifier-like string property, or null.
    private static string? Identifier(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > 60)
        {
            return null;
        }

        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
            {
                return null;
            }
        }

        return text;
    }

    // Retry-After (capped at 5 s) when the provider sent one, else Ai:RetryBackoffMs.
    private static TimeSpan ChooseDelay(TimeSpan? retryAfter, int backoffMs)
    {
        if (retryAfter is { } ra && ra > TimeSpan.Zero)
        {
            return ra > TimeSpan.FromSeconds(MaxRetryAfterSeconds) ? TimeSpan.FromSeconds(MaxRetryAfterSeconds) : ra;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, backoffMs));
    }
}
