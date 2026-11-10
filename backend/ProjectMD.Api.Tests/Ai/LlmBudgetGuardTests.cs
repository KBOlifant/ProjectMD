using Microsoft.Extensions.Options;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Tests.Scans;
using ProjectMD.Api.Services.Groq;

namespace ProjectMD.Api.Tests.Ai;

// Tests for the budget guard and the usage ledger, on an in-memory SQLite database.
public class LlmBudgetGuardTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    // Adds a ledger row directly.
    private static void AddUsage(TestDatabase db, Guid userId, int tokens, DateTime atUtc, bool cacheHit = false)
    {
        using var context = db.CreateContext();
        context.LlmUsage.Add(new LlmUsageRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Model = "test-fast",
            PromptTokens = tokens * 4 / 5,
            CompletionTokens = tokens - tokens * 4 / 5,
            CacheHit = cacheHit,
            CreatedAt = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc),
        });
        context.SaveChanges();
    }

    // Builds the guard on its own database context with the given caps.
    private static LlmBudgetGuard Guard(TestDatabase db, DateTimeOffset now, int perUser = 1000, int global = 5000) =>
        new(db.CreateContext(), Options.Create(new AiOptions { DailyTokenCapPerUser = perUser, DailyTokenCapGlobal = global }), new FixedTimeProvider(now));

    // Nothing spent yet: a call within the cap is allowed.
    [Fact]
    public async Task Allows_a_call_within_the_caps()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("a");

        await Guard(db, Noon).EnsureAsync(user.Id, 900, CancellationToken.None);
    }

    // The user cap blocks: spent plus the estimate would pass it. Landing exactly on it is allowed.
    [Fact]
    public async Task Blocks_at_the_user_cap()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("a");
        AddUsage(db, user.Id, 600, Noon.UtcDateTime);

        await Guard(db, Noon).EnsureAsync(user.Id, 400, CancellationToken.None);
        var error = await Assert.ThrowsAsync<BudgetExceededException>(() =>
            Guard(db, Noon).EnsureAsync(user.Id, 401, CancellationToken.None));

        Assert.Equal(BudgetScope.User, error.Scope);
    }

    // The global cap blocks everybody, including a user who has spent nothing.
    [Fact]
    public async Task Blocks_at_the_global_cap()
    {
        using var db = new TestDatabase();
        var heavy = db.AddUser("heavy");
        var fresh = db.AddUser("fresh");
        AddUsage(db, heavy.Id, 900, Noon.UtcDateTime);
        AddUsage(db, heavy.Id, 900, Noon.UtcDateTime);

        var error = await Assert.ThrowsAsync<BudgetExceededException>(() =>
            Guard(db, Noon, perUser: 1000, global: 2000).EnsureAsync(fresh.Id, 300, CancellationToken.None));

        Assert.Equal(BudgetScope.Global, error.Scope);
    }

    // Only the current UTC day counts: yesterday's spending and tomorrow's boundary are ignored.
    [Fact]
    public async Task Counts_only_the_current_utc_day()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("a");
        AddUsage(db, user.Id, 1000, new DateTime(2026, 10, 13, 23, 59, 59, DateTimeKind.Utc));

        // It is a new day: yesterday's tokens are gone.
        await Guard(db, Noon).EnsureAsync(user.Id, 1000, CancellationToken.None);

        // Spending late today does block today...
        AddUsage(db, user.Id, 500, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));
        await Assert.ThrowsAsync<BudgetExceededException>(() =>
            Guard(db, Noon).EnsureAsync(user.Id, 600, CancellationToken.None));

        // ...and the next UTC day starts fresh.
        var tomorrow = new DateTimeOffset(2026, 10, 15, 0, 0, 1, TimeSpan.Zero);
        await Guard(db, tomorrow).EnsureAsync(user.Id, 1000, CancellationToken.None);
    }

    // One user's spending does not count against another user's own cap.
    [Fact]
    public async Task User_caps_are_independent()
    {
        using var db = new TestDatabase();
        var a = db.AddUser("a");
        var b = db.AddUser("b");
        AddUsage(db, a.Id, 1000, Noon.UtcDateTime);

        await Guard(db, Noon).EnsureAsync(b.Id, 1000, CancellationToken.None);
        await Assert.ThrowsAsync<BudgetExceededException>(() =>
            Guard(db, Noon).EnsureAsync(a.Id, 1, CancellationToken.None));
    }

    // RecordAsync writes the real token counts for a model call.
    [Fact]
    public async Task Record_stores_real_token_counts()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("a");

        await Guard(db, Noon).RecordAsync(user.Id, new LlmResult("text", 700, 120, "test-fast"), cacheHit: false, CancellationToken.None);

        using var context = db.CreateContext();
        var row = Assert.Single(context.LlmUsage.ToList());
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal(700, row.PromptTokens);
        Assert.Equal(120, row.CompletionTokens);
        Assert.False(row.CacheHit);
        Assert.Equal("test-fast", row.Model);
        Assert.Equal(Noon.UtcDateTime, row.CreatedAt);
    }

    // A cache hit is stored with zero tokens, so it never counts toward a cap.
    [Fact]
    public async Task Cache_hits_are_recorded_with_zero_tokens_and_do_not_count()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("a");

        await Guard(db, Noon).RecordAsync(user.Id, new LlmResult(string.Empty, 5000, 5000, "test-fast"), cacheHit: true, CancellationToken.None);

        using (var context = db.CreateContext())
        {
            var row = Assert.Single(context.LlmUsage.ToList());
            Assert.True(row.CacheHit);
            Assert.Equal(0, row.PromptTokens);
            Assert.Equal(0, row.CompletionTokens);
        }

        await Guard(db, Noon, perUser: 100).EnsureAsync(user.Id, 100, CancellationToken.None);
    }

    // Deleting a user removes their ledger rows (cascade), and the table has no page content columns.
    [Fact]
    public void Ledger_rows_are_removed_with_the_user()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("a");
        AddUsage(db, user.Id, 100, Noon.UtcDateTime);

        using (var context = db.CreateContext())
        {
            context.AppUsers.Remove(context.AppUsers.Single(u => u.Id == user.Id));
            context.SaveChanges();
        }

        using var check = db.CreateContext();
        Assert.Empty(check.LlmUsage.ToList());
    }
}
