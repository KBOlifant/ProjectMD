using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ProjectMD.Api.Services.Ai;

// The real ILlmClient: one call to Groq's OpenAI-compatible chat completions endpoint
// (POST {Ai:BaseUrl}/chat/completions, bearer key). Fields confirmed from Groq's docs:
// request model, messages, max_completion_tokens, temperature, reasoning_effort (low|medium|high
// for openai/gpt-oss-20b), include_reasoning; response choices[0].message.content,
// choices[0].finish_reason, usage.prompt_tokens and usage.completion_tokens.
//
// Credit rules: temperature 0, low reasoning effort by default, reasoning text switched off
// (include_reasoning=false), a hard output cap of the answer allowance plus a small reasoning
// headroom, no streaming, and no retry except one for a 429/5xx. A cut-off or empty answer is
// returned as empty text WITH the real token counts, so the pipeline falls back to the basic
// document and still records what was spent.
//
// Secrecy: the key is read from configuration and only ever put in the Authorization header of the
// request. It is never logged, never in an exception message, and neither are prompts or response
// bodies; only status, model, token counts and latency are logged.
public sealed class GroqLlmClient : ILlmClient
{
    // Name of the HttpClient registered for this client.
    public const string HttpClientName = "groq";

    // The safe message shown (and logged) when Groq refuses the key.
    public const string RejectedCredentialsMessage = "The AI service rejected the server's credentials";

    // Longest Retry-After we will honour before the one retry.
    private const int MaxRetryAfterSeconds = 5;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<GroqLlmClient> _logger;

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

        var body = BuildBody(request, options);
        var url = options.BaseUrl.TrimEnd('/') + "/chat/completions";

        for (var attempt = 0; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            var (status, text, retryAfter) = await SendOnceAsync(url, body, options, ct);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (status is >= 200 and < 300)
            {
                return Parse(text, request.Model, status, elapsedMs);
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

            _logger.LogWarning("AI provider refused the request (status {Status}, model {Model})", status, request.Model);
            throw new LlmProviderException($"The AI service rejected the request (status {status})", status, isTransient: false);
        }
    }

    // The JSON sent to Groq. Temperature is always 0; reasoning text is switched off; the output cap
    // is the request's cap plus the reasoning headroom (the same total the budget reserves).
    private static string BuildBody(LlmRequest request, AiOptions options)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["messages"] = new object[]
            {
                new Dictionary<string, string> { ["role"] = "system", ["content"] = request.System },
                new Dictionary<string, string> { ["role"] = "user", ["content"] = request.User },
            },
            ["max_completion_tokens"] = request.MaxOutputTokens + Math.Max(0, options.ReasoningTokenHeadroom),
            ["temperature"] = 0,
            ["reasoning_effort"] = options.ReasoningEffort,
            ["include_reasoning"] = false,
            ["stream"] = false,
        };
        return JsonSerializer.Serialize(payload);
    }

    // One HTTP exchange with the timeout applied. Returns status, body text and Retry-After. Throws
    // LlmProviderException (not transient) for a timeout or a network failure.
    private async Task<(int Status, string Text, TimeSpan? RetryAfter)> SendOnceAsync(
        string url, string body, AiOptions options, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var http = _httpFactory.CreateClient(HttpClientName);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);

            TimeSpan? retryAfter = null;
            var header = response.Headers.RetryAfter;
            if (header?.Delta is { } delta)
            {
                retryAfter = delta;
            }

            return ((int)response.StatusCode, text, retryAfter);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI provider call timed out after {Seconds} s", options.TimeoutSeconds);
            throw new LlmProviderException("The AI service did not answer in time", null, isTransient: false);
        }
        catch (HttpRequestException ex)
        {
            // Only the exception type is logged: nothing a proxy or provider echoes back.
            _logger.LogWarning("AI provider could not be reached ({ErrorType})", ex.GetType().Name);
            throw new LlmProviderException("The AI service could not be reached", null, isTransient: false);
        }
    }

    // Turns a 2xx body into an LlmResult. A cut-off (finish_reason "length") or empty answer comes
    // back with EMPTY text and the real token counts, so it counts as a failed call but is recorded.
    private LlmResult Parse(string json, string model, int status, long elapsedMs)
    {
        string content = string.Empty;
        string? finish = null;
        int promptTokens = 0, completionTokens = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                promptTokens = ReadInt(usage, "prompt_tokens");
                completionTokens = ReadInt(usage, "completion_tokens");
            }

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String)
                {
                    finish = f.GetString();
                }

                if (first.TryGetProperty("message", out var msg)
                    && msg.ValueKind == JsonValueKind.Object
                    && msg.TryGetProperty("content", out var c)
                    && c.ValueKind == JsonValueKind.String)
                {
                    content = c.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            throw new LlmProviderException("The AI service returned an unreadable answer", status, isTransient: false);
        }

        _logger.LogInformation(
            "AI call finished: status {Status}, model {Model}, prompt tokens {PromptTokens}, completion tokens {CompletionTokens}, finish {Finish}, {ElapsedMs} ms",
            status, model, promptTokens, completionTokens, finish ?? "unknown", elapsedMs);

        if (string.Equals(finish, "length", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("AI answer was cut off or empty (finish {Finish}); using the basic document", finish ?? "unknown");
            return new LlmResult(string.Empty, promptTokens, completionTokens, model);
        }

        return new LlmResult(content, promptTokens, completionTokens, model);
    }

    // Reads a non-negative integer property, or 0 when missing.
    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? Math.Max(0, n)
            : 0;

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
