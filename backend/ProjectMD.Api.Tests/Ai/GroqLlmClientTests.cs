using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Tests.Ai;

// A fake network layer: answers with the queued replies in order and records every request. No
// real network is ever used.
internal sealed class FakeGroqHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _replies = new();

    // Requests seen, with their bodies read.
    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

    // Queues a plain reply.
    public FakeGroqHandler Reply(HttpStatusCode status, string body = "{}", Action<HttpResponseMessage>? tweak = null)
    {
        _replies.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            tweak?.Invoke(response);
            return Task.FromResult(response);
        });
        return this;
    }

    // Queues a reply that never arrives until cancelled (a hung server).
    public FakeGroqHandler Hang()
    {
        _replies.Enqueue(async request =>
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(5));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        return this;
    }

    // Records the request and returns the next queued reply.
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Seen.Add((request, body));
        if (_replies.Count == 0)
        {
            throw new InvalidOperationException("Unexpected extra request");
        }

        var reply = _replies.Dequeue();
        var task = reply(request);
        // A hanging reply must still end when the client's timeout cancels the token.
        var cancel = new TaskCompletionSource();
        using var reg = cancellationToken.Register(() => cancel.TrySetCanceled(cancellationToken));
        var finished = await Task.WhenAny(task, cancel.Task);
        if (finished == task)
        {
            return await task;
        }

        throw new OperationCanceledException(cancellationToken);
    }
}

// Hands out an HttpClient on top of the fake handler.
internal sealed class FakeHttpFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    // A new client sharing the fake handler (not disposed with the client).
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

// Keeps every log line (message text and formatted exception) so tests can check for leaks.
internal sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
{
    public List<string> Lines { get; } = [];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add($"{logLevel}: {formatter(state, exception)} {exception}");

    public void Dispose()
    {
    }
}

// Tests for the Groq client with a fake network, plus how the client is chosen at startup.
public class GroqLlmClientTests
{
    // A test-only placeholder; it is not a real key.
    private const string FakeKey = "test-key-DO-NOT-LEAK-1234";

    private static string Success(string content = "## Summary\nAcme.", string finish = "stop", int prompt = 900, int completion = 300) =>
        JsonSerializer.Serialize(new
        {
            id = "x",
            model = "openai/gpt-oss-20b",
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = finish } },
            usage = new { prompt_tokens = prompt, completion_tokens = completion, total_tokens = prompt + completion },
        });

    private static (GroqLlmClient Client, CapturingLoggerProvider Logs) Create(FakeGroqHandler handler, Action<AiOptions>? configure = null)
    {
        var ai = new AiOptions { ApiKey = FakeKey, FastModel = "openai/gpt-oss-20b", RetryBackoffMs = 0, TimeoutSeconds = 30 };
        configure?.Invoke(ai);
        var logs = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        return (new GroqLlmClient(new FakeHttpFactory(handler), Options.Create(ai), factory.CreateLogger<GroqLlmClient>()), logs);
    }

    private static LlmRequest Request() => new("SYSTEM PROMPT", "USER PAGE FACTS", "openai/gpt-oss-20b", 700);

    // URL, bearer header, model, caps, temperature, reasoning settings.
    [Fact]
    public async Task Request_has_the_documented_shape()
    {
        var handler = new FakeGroqHandler().Reply(HttpStatusCode.OK, Success());
        var (client, _) = Create(handler);

        await client.CompleteAsync(Request(), CancellationToken.None);

        var (req, body) = Assert.Single(handler.Seen);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal(FakeKey, req.Headers.Authorization.Parameter);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("openai/gpt-oss-20b", root.GetProperty("model").GetString());
        Assert.Equal(1100, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(0, root.GetProperty("temperature").GetInt32());
        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.GetProperty("include_reasoning").GetBoolean());
        Assert.False(root.GetProperty("stream").GetBoolean());
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("USER PAGE FACTS", messages[1].GetProperty("content").GetString());
    }

    // Text and the provider's real token counts are returned.
    [Fact]
    public async Task Text_and_usage_are_parsed()
    {
        var (client, _) = Create(new FakeGroqHandler().Reply(HttpStatusCode.OK, Success("Hello", prompt: 123, completion: 45)));

        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("Hello", result.Text);
        Assert.Equal(123, result.PromptTokens);
        Assert.Equal(45, result.CompletionTokens);
        Assert.Equal("openai/gpt-oss-20b", result.Model);
    }

    // The effort and headroom come from settings.
    [Fact]
    public async Task Effort_and_headroom_follow_settings()
    {
        var handler = new FakeGroqHandler().Reply(HttpStatusCode.OK, Success());
        var (client, _) = Create(handler, o => { o.ReasoningEffort = "medium"; o.ReasoningTokenHeadroom = 50; });

        await client.CompleteAsync(Request(), CancellationToken.None);

        using var json = JsonDocument.Parse(handler.Seen[0].Body);
        Assert.Equal("medium", json.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal(750, json.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    // A cut-off answer becomes empty text, but the tokens spent are kept. No retry.
    [Fact]
    public async Task Truncated_answer_is_empty_with_real_tokens_and_not_retried()
    {
        var handler = new FakeGroqHandler().Reply(HttpStatusCode.OK, Success("half an answ", finish: "length", prompt: 800, completion: 1100));
        var (client, _) = Create(handler);

        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(800, result.PromptTokens);
        Assert.Equal(1100, result.CompletionTokens);
        Assert.Single(handler.Seen);
    }

    // An empty answer is also empty text with tokens.
    [Fact]
    public async Task Empty_answer_is_empty_text()
    {
        var (client, _) = Create(new FakeGroqHandler().Reply(HttpStatusCode.OK, Success("  ", completion: 400)));

        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(400, result.CompletionTokens);
    }

    // The pipeline falls back to the basic document and records the tokens of a cut-off answer.
    [Fact]
    public async Task Pipeline_falls_back_and_records_tokens_for_a_truncated_answer()
    {
        var handler = new FakeGroqHandler().Reply(HttpStatusCode.OK, Success("cut", finish: "length", prompt: 800, completion: 1100));
        var (client, _) = Create(handler);
        using var harness = new PipelineHarness(ScannerMode.Ai, null);
        var generator = harness.CreateGenerator(client);

        var scan = await generator.GenerateAsync(new Uri("https://acme.example/"), CancellationToken.None);

        Assert.Contains("Basic scan", scan.Markdown);
        var row = Assert.Single(harness.UsageRows());
        Assert.Equal(800, row.PromptTokens);
        Assert.Equal(1100, row.CompletionTokens);
        Assert.Single(handler.Seen);
    }

    // 401 and 403 give the safe message, never the key or the response body, and no retry.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_credentials_throw_a_safe_exception(HttpStatusCode status)
    {
        var handler = new FakeGroqHandler().Reply(status, "{\"error\":{\"message\":\"Invalid API Key SECRET-BODY\"}}");
        var (client, logs) = Create(handler);

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal("The AI service rejected the server's credentials", ex.Message);
        Assert.False(ex.IsTransient);
        Assert.Single(handler.Seen);
        Assert.DoesNotContain(FakeKey, ex.ToString());
        Assert.DoesNotContain("SECRET-BODY", ex.ToString());
        Assert.All(logs.Lines, l => Assert.DoesNotContain("SECRET-BODY", l));
    }

    // One 429 then success: exactly one retry.
    [Fact]
    public async Task One_429_is_retried_once()
    {
        var handler = new FakeGroqHandler()
            .Reply(HttpStatusCode.TooManyRequests)
            .Reply(HttpStatusCode.OK, Success("Fine"));
        var (client, _) = Create(handler);

        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("Fine", result.Text);
        Assert.Equal(2, handler.Seen.Count);
    }

    // Retry-After is honoured but capped at 5 s (here a tiny value so the test stays fast).
    [Fact]
    public async Task Retry_after_is_honoured()
    {
        var handler = new FakeGroqHandler()
            .Reply(HttpStatusCode.ServiceUnavailable, "{}", r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1)))
            .Reply(HttpStatusCode.OK, Success());
        var (client, _) = Create(handler);
        var started = DateTime.UtcNow;

        await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(900));
    }

    // Two 429s: one retry only, then a provider exception that the pipeline will not retry again.
    [Fact]
    public async Task Two_429s_fail_after_one_retry_and_the_pipeline_falls_back()
    {
        var handler = new FakeGroqHandler().Reply(HttpStatusCode.TooManyRequests).Reply(HttpStatusCode.TooManyRequests);
        var (client, _) = Create(handler);
        using var harness = new PipelineHarness(ScannerMode.Ai, null);

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => client.CompleteAsync(Request(), CancellationToken.None));
        Assert.False(ex.IsTransient);
        Assert.Equal(2, handler.Seen.Count);

        var handler2 = new FakeGroqHandler().Reply(HttpStatusCode.InternalServerError).Reply(HttpStatusCode.BadGateway);
        var (client2, _) = Create(handler2);
        var scan = await harness.CreateGenerator(client2).GenerateAsync(new Uri("https://acme.example/"), CancellationToken.None);
        Assert.Contains("Basic scan", scan.Markdown);
        Assert.Equal(2, handler2.Seen.Count);
        Assert.Empty(harness.UsageRows());
    }

    // A server that never answers ends at Ai:TimeoutSeconds with a provider exception.
    [Fact]
    public async Task A_hung_server_times_out()
    {
        var (client, _) = Create(new FakeGroqHandler().Hang(), o => o.TimeoutSeconds = 1);

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Contains("in time", ex.Message);
    }

    // A caller cancellation is not turned into a provider error.
    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var (client, _) = Create(new FakeGroqHandler().Hang());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(Request(), cts.Token));
    }

    // The budget reserves the answer allowance PLUS the headroom for this client, and only the
    // allowance for other clients.
    [Fact]
    public async Task Budget_reservation_includes_the_headroom()
    {
        var ai = new AiOptions { FastModel = "m" };
        Assert.Equal(1100, ai.OutputTokenCap(true));
        Assert.Equal(700, ai.OutputTokenCap(false));

        // A user cap that fits prompt + answer allowance (+100) but not prompt + allowance +
        // headroom: only the headroom-adding client is stopped by the budget guard.
        var request = new PromptBuilder().Build(Digests.Simple(), [], ai);
        var prompt = TokenEstimator.Estimate(request.System) + TokenEstimator.Estimate(request.User);
        var cap = prompt + 800;
        var withHeadroom = new Probe(true);
        var without = new Probe(false);
        using var c = new PipelineHarness(ScannerMode.Ai, null, configure: o => o.DailyTokenCapPerUser = cap);
        using var d = new PipelineHarness(ScannerMode.Ai, null, configure: o => o.DailyTokenCapPerUser = cap);

        var stopped = await c.CreateGenerator(withHeadroom).GenerateAsync(new Uri("https://acme.example/"), CancellationToken.None);
        await d.CreateGenerator(without).GenerateAsync(new Uri("https://acme.example/"), CancellationToken.None);

        Assert.Equal(0, withHeadroom.Calls);
        Assert.Equal(1, without.Calls);
        Assert.Contains("Basic scan", stopped.Markdown);
        await Task.CompletedTask;
    }

    // Neither the key nor prompts nor bodies appear in log output.
    [Fact]
    public async Task Logs_never_contain_the_key_prompts_or_bodies()
    {
        var handler = new FakeGroqHandler()
            .Reply(HttpStatusCode.TooManyRequests, "{\"echo\":\"BODY-ECHO\"}")
            .Reply(HttpStatusCode.OK, Success("ANSWER-TEXT-XYZ"));
        var (client, logs) = Create(handler);

        await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.NotEmpty(logs.Lines);
        foreach (var line in logs.Lines)
        {
            Assert.DoesNotContain(FakeKey, line);
            Assert.DoesNotContain("Bearer", line);
            Assert.DoesNotContain("USER PAGE FACTS", line);
            Assert.DoesNotContain("ANSWER-TEXT-XYZ", line);
            Assert.DoesNotContain("BODY-ECHO", line);
        }
    }

    private static ServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScanServices(configuration);
        return services;
    }

    // Groq client only when the mode is ai AND the key is non-empty.
    [Theory]
    [InlineData("ai", "some-key", true)]
    [InlineData("ai", "", false)]
    [InlineData("ai", "   ", false)]
    [InlineData("extract", "some-key", false)]
    [InlineData("static", "some-key", false)]
    public void Client_is_chosen_by_mode_and_key(string mode, string key, bool expectGroq)
    {
        var settings = new Dictionary<string, string?> { ["Scanner:Mode"] = mode, ["Ai:ApiKey"] = key, ["Ai:FastModel"] = "m" };
        using var provider = Register(settings).BuildServiceProvider();

        var client = provider.GetRequiredService<ILlmClient>();

        if (expectGroq)
        {
            Assert.IsType<GroqLlmClient>(client);
        }
        else
        {
            Assert.IsType<NotConfiguredLlmClient>(client);
        }
    }

    // The Groq client needs Ai:FastModel and the error names the key; effort is validated.
    [Fact]
    public void Startup_validation_names_the_missing_model_and_bad_effort()
    {
        using var provider = Register(new() { ["Scanner:Mode"] = "ai", ["Ai:ApiKey"] = "k", ["Ai:ReasoningEffort"] = "turbo" }).BuildServiceProvider();

        var error = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Ai:FastModel", error.ToString());
        Assert.Contains("Ai:ReasoningEffort", error.ToString());
    }

    // The documented defaults.
    [Fact]
    public void Groq_defaults()
    {
        var ai = new AiOptions();

        Assert.Equal("https://api.groq.com/openai/v1", ai.BaseUrl);
        Assert.Equal("low", ai.ReasoningEffort);
        Assert.Equal(400, ai.ReasoningTokenHeadroom);
        Assert.Equal(30, ai.TimeoutSeconds);
        Assert.Equal(string.Empty, ai.ApiKey);
    }

    // OPT-IN live check: runs only with PROJECTMD_LIVE_GROQ=1 and Ai__ApiKey in the environment.
    // Makes one tiny call and asserts on shape only.
    [LiveGroqFact]
    public async Task Live_call_has_the_expected_shape()
    {
        var ai = new AiOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("Ai__ApiKey")!,
            FastModel = Environment.GetEnvironmentVariable("Ai__FastModel") ?? "openai/gpt-oss-20b",
        };
        var services = new ServiceCollection();
        services.AddHttpClient(GroqLlmClient.HttpClientName);
        using var provider = services.BuildServiceProvider();
        var client = new GroqLlmClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(ai),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GroqLlmClient>.Instance);

        var result = await client.CompleteAsync(new LlmRequest("Reply with one word.", "Say hi.", ai.FastModel, 20), CancellationToken.None);

        Assert.True(result.PromptTokens > 0);
        Assert.True(result.CompletionTokens > 0);
        Assert.Equal(ai.FastModel, result.Model);
    }

    // A client that records requests and answers fixed prose; can claim the reasoning headroom.
    private sealed class Probe : ILlmClient
    {
        private readonly bool _headroom;

        public Probe(bool headroom)
        {
            _headroom = headroom;
        }

        public int Calls { get; private set; }

        public LlmRequest? LastRequest { get; private set; }

        public bool AddsReasoningHeadroom => _headroom;

        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new LlmResult("## Summary\nAcme.", 500, 100, request.Model));
        }
    }
}

// A [Fact] skipped unless PROJECTMD_LIVE_GROQ=1 and Ai__ApiKey is present in the environment.
public sealed class LiveGroqFactAttribute : FactAttribute
{
    public LiveGroqFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROJECTMD_LIVE_GROQ") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Ai__ApiKey")))
        {
            Skip = "Set PROJECTMD_LIVE_GROQ=1 and Ai__ApiKey to run the live Groq test.";
        }
    }
}
