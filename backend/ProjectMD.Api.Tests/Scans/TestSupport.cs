using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Tests.Scans;

// A clock that always says the same time, so tests can pick a month and check the quota boundaries.
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    // Fixes the clock at the given UTC time.
    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    // Always returns the fixed time.
    public override DateTimeOffset GetUtcNow() => _now;
}

// An in-memory SQLite database with the real AppDbContext model, for tests that need a database
// but not the web host. Never connects to any real database.
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    // Opens a private in-memory database and creates the tables.
    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    // A new context on the shared connection (so data written by one context is visible to the next).
    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    // Adds a user and returns it.
    public AppUser AddUser(string uid)
    {
        using var db = CreateContext();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FirebaseUid = uid,
            Email = uid + "@example.com",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);
        db.SaveChanges();
        return user;
    }

    // Adds a scan for the user, created at the given UTC time. Pass deletedAtUtc to add a scan the
    // user has already cleared from their history (a soft-deleted row).
    public Scan AddScan(Guid userId, DateTime createdAtUtc, string host = "example.com", DateTime? deletedAtUtc = null)
    {
        using var db = CreateContext();
        var scan = NewScan(userId, createdAtUtc, host, deletedAtUtc);
        db.Scans.Add(scan);
        db.SaveChanges();
        return scan;
    }

    // Clears the user's visible history the way the API does (sets DeletedAt, keeps the rows) and
    // returns how many scans were cleared.
    public int SoftDeleteScans(Guid userId, DateTime whenUtc)
    {
        using var db = CreateContext();
        var when = DateTime.SpecifyKind(whenUtc, DateTimeKind.Utc);
        return db.Scans
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .ExecuteUpdate(set => set.SetProperty(s => s.DeletedAt, when));
    }

    // Builds a small valid Scan row (soft deleted when deletedAtUtc is given).
    public static Scan NewScan(Guid userId, DateTime createdAtUtc, string host = "example.com", DateTime? deletedAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Url = "https://" + host + "/",
        Host = host,
        CreatedAt = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        DeletedAt = deletedAtUtc is null ? null : DateTime.SpecifyKind(deletedAtUtc.Value, DateTimeKind.Utc),
        DurationMs = 3000,
        Confidence = 90,
        Pages = 10,
        Tech = [new ScanTech { Name = "Next.js 15", Highlighted = true }, new ScanTech { Name = "React 19", Highlighted = false }],
        Markdown = "# " + host + " Documentation\n",
    };

    // Closes the database (and so deletes it).
    public void Dispose() => _connection.Dispose();
}
