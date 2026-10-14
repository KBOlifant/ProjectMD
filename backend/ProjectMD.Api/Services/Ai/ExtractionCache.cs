using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Services.Ai;

// A cache of AI-written prose, keyed by page content hash + prompt version + model. A hit means
// zero credits. The stored text is the same for every user, because it is derived from public page
// content only (the prompt contains no user data).
public interface IExtractionCache
{
    // Returns the cached prose, or null when there is none, it has expired, or the cache is off
    // (Ai:CacheDays = 0). A hit also adds one to the entry's HitCount.
    Task<string?> TryGetAsync(string contentHash, int promptVersion, string model, CancellationToken ct);

    // Saves (or refreshes) the prose for the key, valid for Ai:CacheDays. Does nothing when the
    // cache is off or the key does not fit the columns. Never throws because another request stored
    // the same key at the same moment.
    Task StoreAsync(string contentHash, int promptVersion, string model, string markdown, CancellationToken ct);
}

// The database-backed IExtractionCache (table ExtractionCache). Expired rows are ignored when
// reading and overwritten on the next store; there is no purge job yet.
public sealed class DbExtractionCache : IExtractionCache
{
    // Column limits (see AppDbContext).
    private const int MaxHashLength = 64;
    private const int MaxModelLength = AiOptionsValidator.MaxModelNameLength;

    private readonly AppDbContext _db;
    private readonly IOptions<AiOptions> _options;
    private readonly TimeProvider _clock;

    // Receives the database, the AI settings and the clock (replaceable in tests).
    public DbExtractionCache(AppDbContext db, IOptions<AiOptions> options, TimeProvider clock)
    {
        _db = db;
        _options = options;
        _clock = clock;
    }

    // Looks the key up; if a live row exists, bumps HitCount in the database and returns its text.
    public async Task<string?> TryGetAsync(string contentHash, int promptVersion, string model, CancellationToken ct)
    {
        if (!CanUse(contentHash, model))
        {
            return null;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var markdown = await _db.ExtractionCache
            .AsNoTracking()
            .Where(e => e.ContentHash == contentHash && e.PromptVersion == promptVersion && e.Model == model && e.ExpiresAt > now)
            .Select(e => e.Markdown)
            .FirstOrDefaultAsync(ct);
        if (markdown is null)
        {
            return null;
        }

        await _db.ExtractionCache
            .Where(e => e.ContentHash == contentHash && e.PromptVersion == promptVersion && e.Model == model)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.HitCount, e => e.HitCount + 1), ct);
        return markdown;
    }

    // Inserts the row, or refreshes an existing (usually expired) one. If another request inserted
    // the same key first, that is fine: the content is identical, so the loser just stops.
    public async Task StoreAsync(string contentHash, int promptVersion, string model, string markdown, CancellationToken ct)
    {
        if (!CanUse(contentHash, model) || string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddDays(_options.Value.CacheDays);

        var updated = await _db.ExtractionCache
            .Where(e => e.ContentHash == contentHash && e.PromptVersion == promptVersion && e.Model == model)
            .ExecuteUpdateAsync(set => set
                .SetProperty(e => e.Markdown, markdown)
                .SetProperty(e => e.CreatedAt, now)
                .SetProperty(e => e.ExpiresAt, expires)
                .SetProperty(e => e.HitCount, 0), ct);
        if (updated > 0)
        {
            return;
        }

        var entry = new ExtractionCacheEntry
        {
            ContentHash = contentHash,
            PromptVersion = promptVersion,
            Model = model,
            Markdown = markdown,
            CreatedAt = now,
            ExpiresAt = expires,
            HitCount = 0,
        };
        _db.ExtractionCache.Add(entry);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with an identical insert. Forget our copy; the winner's row is as good.
            _db.Entry(entry).State = EntityState.Detached;
        }
    }

    // True when the cache is on and the key fits the columns.
    private bool CanUse(string contentHash, string model) =>
        _options.Value.CacheDays > 0
        && contentHash.Length is > 0 and <= MaxHashLength
        && model.Length is > 0 and <= MaxModelLength;
}
