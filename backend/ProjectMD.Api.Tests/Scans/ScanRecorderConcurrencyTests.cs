using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Tests.Scans;

// Checks that requests arriving at the same moment cannot push a user past the monthly limit.
// Uses a throwaway SQLite file with one connection per request (never a real database). SQLite
// locks the whole file, so this proves the check-then-insert is one serialised unit; on PostgreSQL
// the same code serialises per user through the row lock taken by the first UPDATE.
public class ScanRecorderConcurrencyTests
{
    // What a parallel run ended up with.
    private sealed record RaceResult(int Saved, int Refused, int VisibleRows, int AllRows);

    // Fires `attempts` simultaneous creates (limit `limit`) plus `clears` simultaneous "clear
    // history" calls for one user against a throwaway SQLite file, then reports the outcome.
    private static async Task<RaceResult> RunRaceAsync(int limit, int attempts, int clears)
    {
        var path = Path.Combine(Path.GetTempPath(), $"projectmd-recorder-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False";
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var userId = Guid.NewGuid();

        AppDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);

        try
        {
            using (var setup = NewContext())
            {
                await setup.Database.EnsureCreatedAsync();
                setup.AppUsers.Add(new AppUser
                {
                    Id = userId,
                    FirebaseUid = "racer",
                    CreatedAt = now.UtcDateTime,
                    LastSeenAt = now.UtcDateTime,
                });
                await setup.SaveChangesAsync();
            }

            var creates = Enumerable.Range(0, attempts).Select(_ => Task.Run(async () =>
            {
                using var db = NewContext();
                var usage = new UsageService(db, Options.Create(new UsageOptions { MonthlyLimit = limit }), clock);
                var recorder = new ScanRecorder(db, usage, clock);
                return await recorder.TryRecordAsync(TestDatabase.NewScan(userId, now.UtcDateTime), CancellationToken.None);
            }));

            // The same statement ScansController.Clear runs: soft delete the user's visible scans.
            var clearing = Enumerable.Range(0, clears).Select(_ => Task.Run(async () =>
            {
                using var db = NewContext();
                var when = now.UtcDateTime;
                await db.Scans
                    .Where(s => s.UserId == userId && s.DeletedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.DeletedAt, when));
            }));

            var results = await Task.WhenAll(creates);
            await Task.WhenAll(clearing);

            using var check = NewContext();
            return new RaceResult(
                results.Count(saved => saved),
                results.Count(saved => !saved),
                await check.Scans.CountAsync(),
                await check.Scans.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    // Many simultaneous creates with a limit of 3 save exactly 3 scans and refuse the rest.
    [Fact]
    public async Task Parallel_creates_never_exceed_the_limit()
    {
        const int limit = 3;
        const int attempts = 8;

        var result = await RunRaceAsync(limit, attempts, clears: 0);

        Assert.Equal(limit, result.Saved);
        Assert.Equal(attempts - limit, result.Refused);
        Assert.Equal(limit, result.AllRows);
        Assert.Equal(limit, result.VisibleRows);
    }

    // Clearing history while creates are racing never frees up allowance: still exactly 3 scans are
    // saved (all rows are kept, cleared ones included), however the clears interleave with them.
    [Fact]
    public async Task Parallel_creates_and_clears_never_exceed_the_limit()
    {
        const int limit = 3;
        const int attempts = 8;

        var result = await RunRaceAsync(limit, attempts, clears: 4);

        Assert.Equal(limit, result.Saved);
        Assert.Equal(attempts - limit, result.Refused);
        Assert.Equal(limit, result.AllRows);
        Assert.InRange(result.VisibleRows, 0, limit);
    }
}
