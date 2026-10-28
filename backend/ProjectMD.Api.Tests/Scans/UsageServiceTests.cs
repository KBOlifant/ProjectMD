using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Tests.Scans;

// Tests for the monthly scan counter (UsageService) and the insert (ScanRecorder), run against an
// in-memory SQLite database. There is no monthly cap: UsageService always reports an unbounded limit.
public class UsageServiceTests
{
    // Builds a usage service for a database and a pretend "now".
    private static UsageService CreateService(TestDatabase database, DateTimeOffset now) =>
        new(database.CreateContext(), new FixedTimeProvider(now));

    // Only scans created in the current UTC month count, and only the user's own.
    [Fact]
    public async Task Counts_only_this_users_scans_in_the_current_utc_month()
    {
        using var database = new TestDatabase();
        var alice = database.AddUser("alice");
        var bob = database.AddUser("bob");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        database.AddScan(alice.Id, new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc));  // last month
        database.AddScan(alice.Id, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));     // first instant of this month
        database.AddScan(alice.Id, new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc));
        database.AddScan(alice.Id, new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc)); // last second of this month
        database.AddScan(alice.Id, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));     // next month
        database.AddScan(bob.Id, new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc));

        var usage = await CreateService(database, now).GetAsync(alice.Id, CancellationToken.None);

        Assert.Equal(3, usage.Used);
        Assert.Equal(int.MaxValue, usage.Limit);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), usage.ResetsAt);
    }

    // Scans the user cleared from their history (soft deleted) still count for the month they were
    // created in; cleared scans from other months and other people's cleared scans do not.
    [Fact]
    public async Task Counts_scans_the_user_cleared_from_their_history()
    {
        using var database = new TestDatabase();
        var alice = database.AddUser("alice");
        var bob = database.AddUser("bob");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var clearedAt = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

        database.AddScan(alice.Id, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc));                          // visible
        database.AddScan(alice.Id, new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));                          // visible
        database.AddScan(alice.Id, new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc), deletedAtUtc: clearedAt); // cleared, this month
        database.AddScan(alice.Id, new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc), deletedAtUtc: clearedAt); // cleared, this month
        database.AddScan(alice.Id, new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc), deletedAtUtc: clearedAt);  // cleared, last month
        database.AddScan(bob.Id, new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc), deletedAtUtc: clearedAt);    // someone else's

        var usage = await CreateService(database, now).GetAsync(alice.Id, CancellationToken.None);

        Assert.Equal(4, usage.Used);
    }

    // Clearing the history does not change the count: the same user reads the same number before
    // and after.
    [Fact]
    public async Task Clearing_history_does_not_change_the_count()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        database.AddScan(user.Id, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc));
        database.AddScan(user.Id, new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));
        var before = await CreateService(database, now).GetAsync(user.Id, CancellationToken.None);

        var cleared = database.SoftDeleteScans(user.Id, now.UtcDateTime);

        var after = await CreateService(database, now).GetAsync(user.Id, CancellationToken.None);
        Assert.Equal(2, cleared);
        Assert.Equal(2, before.Used);
        Assert.Equal(before.Used, after.Used);
    }

    // The month rolls over correctly at the end of the year.
    [Fact]
    public async Task Reset_date_in_december_is_the_first_of_january()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");

        var usage = await CreateService(database, new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero))
            .GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), usage.ResetsAt);
        Assert.Equal(0, usage.Used);
    }

    // A user with no scans has used nothing, and there is no cap on the limit.
    [Fact]
    public async Task New_user_has_used_nothing_and_the_limit_is_unbounded()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");

        var usage = await CreateService(database, DateTimeOffset.UtcNow).GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(0, usage.Used);
        Assert.Equal(int.MaxValue, usage.Limit);
    }

    // The recorder saves every scan; there is no monthly cap to run out of.
    [Fact]
    public async Task Recorder_always_saves()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        async Task<bool> RecordAsync()
        {
            var db = database.CreateContext();
            var clock = new FixedTimeProvider(now);
            var usage = new UsageService(db, clock);
            var recorder = new ScanRecorder(db, usage, clock);
            return await recorder.TryRecordAsync(TestDatabase.NewScan(user.Id, now.UtcDateTime), CancellationToken.None);
        }

        Assert.True(await RecordAsync());
        Assert.True(await RecordAsync());
        Assert.True(await RecordAsync());

        var after = await CreateService(database, now).GetAsync(user.Id, CancellationToken.None);
        Assert.Equal(3, after.Used);
    }

    // The recorder refuses a scan for a user that does not exist instead of inserting it.
    [Fact]
    public async Task Recorder_throws_for_an_unknown_user()
    {
        using var database = new TestDatabase();
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var db = database.CreateContext();
        var clock = new FixedTimeProvider(now);
        var usage = new UsageService(db, clock);
        var recorder = new ScanRecorder(db, usage, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.TryRecordAsync(TestDatabase.NewScan(Guid.NewGuid(), now.UtcDateTime), CancellationToken.None));
    }
}
