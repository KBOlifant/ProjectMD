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
    // Many simultaneous creates with a limit of 3 save exactly 3 scans and refuse the rest.
    [Fact]
    public async Task Parallel_creates_never_exceed_the_limit()
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

            const int limit = 3;
            const int attempts = 8;
            var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => Task.Run(async () =>
            {
                using var db = NewContext();
                var usage = new UsageService(db, Options.Create(new UsageOptions { MonthlyLimit = limit }), clock);
                var recorder = new ScanRecorder(db, usage, clock);
                return await recorder.TryRecordAsync(TestDatabase.NewScan(userId, now.UtcDateTime), CancellationToken.None);
            })));

            Assert.Equal(limit, results.Count(saved => saved));
            Assert.Equal(attempts - limit, results.Count(saved => !saved));

            using var check = NewContext();
            Assert.Equal(limit, await check.Scans.CountAsync());
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
}
