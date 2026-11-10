using ProjectMD.Api.Services.Groq;

namespace ProjectMD.Api.Services.Ai;

// Runs once when the app starts in `ai` mode and says plainly in the log whether AI writing is
// really available. With only NotConfiguredLlmClient registered it logs ONE warning, and scans then
// behave like `extract` mode with the footer "AI writing is not configured". It never stops startup.
public sealed class AiModeStartupNotice : IHostedService
{
    private readonly ILlmClient _llm;
    private readonly ILogger<AiModeStartupNotice> _logger;

    // Receives the registered AI client and a logger.
    public AiModeStartupNotice(ILlmClient llm, ILogger<AiModeStartupNotice> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    // Logs the one-time notice.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_llm is NotConfiguredLlmClient)
        {
            _logger.LogWarning(
                "Scanner:Mode is 'ai' but no AI client is configured. Scans will use the basic extract output " +
                "with the footer 'AI writing is not configured'.");
        }
        else
        {
            _logger.LogInformation("Scanner:Mode is 'ai'; AI writing is on (one call per scan, capped by the Ai settings).");
        }

        return Task.CompletedTask;
    }

    // Nothing to stop.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
