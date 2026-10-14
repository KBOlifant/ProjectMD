using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Services.Ai;

// The hard spending limit. EnsureAsync runs BEFORE every model call; RecordAsync runs after it
// (and after a cache hit) to write the real numbers into the ledger.
public interface ILlmBudgetGuard
{
    // Throws BudgetExceededException if spending `estimatedTokens` more today would take the user
    // past Ai:DailyTokenCapPerUser or everyone past Ai:DailyTokenCapGlobal. "Today" is the current UTC day.
    Task EnsureAsync(Guid userId, int estimatedTokens, CancellationToken ct);

    // Writes one ledger row with the provider's real token counts. For a cache hit the counts are
    // stored as 0 (no credits were spent) and CacheHit is true.
    Task RecordAsync(Guid userId, LlmResult result, bool cacheHit, CancellationToken ct);
}

// The database-backed ILlmBudgetGuard. It sums the LlmUsage rows of the current UTC day. Cache hits
// and rows from earlier days never count.
//
// Known limit: the check and the later record are separate steps, so several requests running at
// the same instant can each pass the check and together overshoot a cap by a few calls. The
// per-user scan rate limit (RateLimiting:ScanCreate) keeps that overshoot small.
public sealed class LlmBudgetGuard : ILlmBudgetGuard
{
    private readonly AppDbContext _db;
    private readonly IOptions<AiOptions> _options;
    private readonly TimeProvider _clock;

    // Receives the database, the AI settings and the clock (replaceable in tests).
    public LlmBudgetGuard(AppDbContext db, IOptions<AiOptions> options, TimeProvider clock)
    {
        _db = db;
        _options = options;
        _clock = clock;
    }

    // Adds up today's tokens for the user and for everyone and compares them to the caps, user cap
    // first. A call that would land exactly on the cap is allowed; one token over is not.
    public async Task EnsureAsync(Guid userId, int estimatedTokens, CancellationToken ct)
    {
        var options = _options.Value;
        var estimate = Math.Max(0, estimatedTokens);
        var (dayStart, dayEnd) = CurrentUtcDay();

        var today = _db.LlmUsage
            .AsNoTracking()
            .Where(u => u.CreatedAt >= dayStart && u.CreatedAt < dayEnd);

        var userSpent = await today
            .Where(u => u.UserId == userId)
            .SumAsync(u => (long)u.PromptTokens + u.CompletionTokens, ct);
        if (userSpent + estimate > options.DailyTokenCapPerUser)
        {
            throw new BudgetExceededException(BudgetScope.User);
        }

        var globalSpent = await today.SumAsync(u => (long)u.PromptTokens + u.CompletionTokens, ct);
        if (globalSpent + estimate > options.DailyTokenCapGlobal)
        {
            throw new BudgetExceededException(BudgetScope.Global);
        }
    }

    // Saves the ledger row. A cache hit is always stored with zero tokens.
    public async Task RecordAsync(Guid userId, LlmResult result, bool cacheHit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        _db.LlmUsage.Add(new LlmUsageRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ScanId = null,
            Model = Clip(result.Model, AiOptionsValidator.MaxModelNameLength),
            PromptTokens = cacheHit ? 0 : Math.Max(0, result.PromptTokens),
            CompletionTokens = cacheHit ? 0 : Math.Max(0, result.CompletionTokens),
            CacheHit = cacheHit,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);
    }

    // The start (inclusive) and end (exclusive) of the current UTC day.
    private (DateTime Start, DateTime End) CurrentUtcDay()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var start = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddDays(1));
    }

    // Cuts text to the column length so a long model name can never fail the insert.
    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
