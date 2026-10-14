using Microsoft.Extensions.Options;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Tests.Scans;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the AI cache (ExtractionCache table), on an in-memory SQLite database.
public class ExtractionCacheTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    // Builds a cache on its own database context.
    private static DbExtractionCache Cache(TestDatabase db, DateTimeOffset now, int cacheDays = 7) =>
        new(db.CreateContext(), Options.Create(new AiOptions { CacheDays = cacheDays }), new FixedTimeProvider(now));

    // Nothing stored: a miss.
    [Fact]
    public async Task Miss_when_nothing_is_stored()
    {
        using var db = new TestDatabase();

        Assert.Null(await Cache(db, Noon).TryGetAsync("h1", 1, "m", CancellationToken.None));
    }

    // Stored text comes back for the same hash, prompt version and model, and counts the hit.
    [Fact]
    public async Task Hit_returns_the_text_and_counts_hits()
    {
        using var db = new TestDatabase();
        await Cache(db, Noon).StoreAsync("h1", 1, "m", "prose", CancellationToken.None);

        Assert.Equal("prose", await Cache(db, Noon).TryGetAsync("h1", 1, "m", CancellationToken.None));
        Assert.Equal("prose", await Cache(db, Noon).TryGetAsync("h1", 1, "m", CancellationToken.None));

        using var context = db.CreateContext();
        var row = Assert.Single(context.ExtractionCache.ToList());
        Assert.Equal(2, row.HitCount);
        Assert.Equal(Noon.UtcDateTime.AddDays(7), row.ExpiresAt);
    }

    // A different hash, prompt version or model is a different key.
    [Fact]
    public async Task Key_is_hash_prompt_version_and_model()
    {
        using var db = new TestDatabase();
        await Cache(db, Noon).StoreAsync("h1", 1, "m", "prose", CancellationToken.None);

        Assert.Null(await Cache(db, Noon).TryGetAsync("h2", 1, "m", CancellationToken.None));
        Assert.Null(await Cache(db, Noon).TryGetAsync("h1", 2, "m", CancellationToken.None));
        Assert.Null(await Cache(db, Noon).TryGetAsync("h1", 1, "other-model", CancellationToken.None));
    }

    // An expired entry is ignored and a store refreshes it in place.
    [Fact]
    public async Task Expired_entries_are_ignored_and_refreshed_by_a_store()
    {
        using var db = new TestDatabase();
        await Cache(db, Noon, cacheDays: 7).StoreAsync("h1", 1, "m", "old", CancellationToken.None);
        var later = Noon.AddDays(8);

        Assert.Null(await Cache(db, later).TryGetAsync("h1", 1, "m", CancellationToken.None));

        await Cache(db, later).StoreAsync("h1", 1, "m", "new", CancellationToken.None);

        Assert.Equal("new", await Cache(db, later).TryGetAsync("h1", 1, "m", CancellationToken.None));
        using var context = db.CreateContext();
        Assert.Single(context.ExtractionCache.ToList());
    }

    // Ai:CacheDays = 0 turns the cache off completely.
    [Fact]
    public async Task Cache_can_be_turned_off()
    {
        using var db = new TestDatabase();
        var off = Cache(db, Noon, cacheDays: 0);

        await off.StoreAsync("h1", 1, "m", "prose", CancellationToken.None);

        Assert.Null(await off.TryGetAsync("h1", 1, "m", CancellationToken.None));
        using var context = db.CreateContext();
        Assert.Empty(context.ExtractionCache.ToList());
    }

    // Keys that do not fit the columns are skipped instead of failing.
    [Fact]
    public async Task Oversized_keys_are_skipped()
    {
        using var db = new TestDatabase();
        var cache = Cache(db, Noon);

        await cache.StoreAsync(new string('h', 65), 1, "m", "prose", CancellationToken.None);
        await cache.StoreAsync("h1", 1, new string('m', 101), "prose", CancellationToken.None);

        using var context = db.CreateContext();
        Assert.Empty(context.ExtractionCache.ToList());
    }
}
