using Microsoft.Extensions.Options;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Tests.Scans;

// Tests for the monthly quota counter (UsageService) and the quota-checked insert (ScanRecorder),
// run against an in-memory SQLite database.
public class UsageServiceTests
{
    // Builds a usage service for a database, a limit and a pretend "now".
    private static UsageService CreateService(TestDatabase database, int limit, DateTimeOffset now) =>
        new(database.CreateContext(), Options.Create(new UsageOptions { MonthlyLimit = limit }), new FixedTimeProvider(now));

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

        var usage = await CreateService(database, 10, now).GetAsync(alice.Id, CancellationToken.None);

        Assert.Equal(3, usage.Used);
        Assert.Equal(10, usage.Limit);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), usage.ResetsAt);
    }

    // The month rolls over correctly at the end of the year.
    [Fact]
    public async Task Reset_date_in_december_is_the_first_of_january()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");

        var usage = await CreateService(database, 10, new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero))
            .GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), usage.ResetsAt);
        Assert.Equal(0, usage.Used);
    }

    // A user with no scans has used nothing, and the limit comes from the options.
    [Fact]
    public async Task New_user_has_used_nothing_and_limit_comes_from_options()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");

        var usage = await CreateService(database, 3, DateTimeOffset.UtcNow).GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(0, usage.Used);
        Assert.Equal(3, usage.Limit);
    }

    // The default monthly limit is 10 when nothing is configured.
    [Fact]
    public void Default_monthly_limit_is_ten()
    {
        Assert.Equal(10, new UsageOptions().MonthlyLimit);
    }

    // The recorder saves scans until the limit is reached and then refuses without saving.
    [Fact]
    public async Task Recorder_saves_until_the_limit_then_refuses()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        async Task<bool> RecordAsync()
        {
            var db = database.CreateContext();
            var clock = new FixedTimeProvider(now);
            var usage = new UsageService(db, Options.Create(new UsageOptions { MonthlyLimit = 2 }), clock);
            var recorder = new ScanRecorder(db, usage, clock);
            return await recorder.TryRecordAsync(TestDatabase.NewScan(user.Id, now.UtcDateTime), CancellationToken.None);
        }

        Assert.True(await RecordAsync());
        Assert.True(await RecordAsync());
        Assert.False(await RecordAsync());

        var after = await CreateService(database, 2, now).GetAsync(user.Id, CancellationToken.None);
        Assert.Equal(2, after.Used);
    }

    // The recorder refuses a scan for a user that does not exist instead of inserting it.
    [Fact]
    public async Task Recorder_throws_for_an_unknown_user()
    {
        using var database = new TestDatabase();
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var db = database.CreateContext();
        var clock = new FixedTimeProvider(now);
        var usage = new UsageService(db, Options.Create(new UsageOptions()), clock);
        var recorder = new ScanRecorder(db, usage, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.TryRecordAsync(TestDatabase.NewScan(Guid.NewGuid(), now.UtcDateTime), CancellationToken.None));
    }
}
