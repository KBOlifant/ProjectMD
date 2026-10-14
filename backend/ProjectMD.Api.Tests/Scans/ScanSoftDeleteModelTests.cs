using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Tests.Scans;

// Tests for how the DbContext treats soft-deleted scans (Scan.DeletedAt set): the global query
// filter hides them from every normal query, and only IgnoreQueryFilters brings them back.
public class ScanSoftDeleteModelTests
{
    // A new scan is visible: DeletedAt is null until the owner clears their history.
    [Fact]
    public void A_new_scan_is_not_deleted()
    {
        Assert.Null(new Scan().DeletedAt);
    }

    // DeletedAt is an optional column, so ordinary scans can leave it empty.
    [Fact]
    public void DeletedAt_is_a_nullable_column()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();

        var property = db.Model.FindEntityType(typeof(Scan))!.FindProperty(nameof(Scan.DeletedAt));

        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        Assert.Equal(typeof(DateTime?), property.ClrType);
    }

    // Normal queries (count, filter by id, includes through the owner) skip soft-deleted scans.
    [Fact]
    public async Task Normal_queries_hide_soft_deleted_scans()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");
        var created = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
        var visible = database.AddScan(user.Id, created, "visible.example.com");
        var deleted = database.AddScan(user.Id, created, "deleted.example.com", deletedAtUtc: created.AddDays(1));
        using var db = database.CreateContext();

        Assert.Equal(1, await db.Scans.CountAsync());
        Assert.NotNull(await db.Scans.FirstOrDefaultAsync(s => s.Id == visible.Id));
        Assert.Null(await db.Scans.FirstOrDefaultAsync(s => s.Id == deleted.Id));
        Assert.Null(await db.Scans.FindAsync(deleted.Id));

        var owner = await db.AppUsers.Include(u => u.Scans).SingleAsync(u => u.Id == user.Id);
        Assert.Equal(new[] { visible.Id }, owner.Scans.Select(s => s.Id));
    }

    // IgnoreQueryFilters brings the soft-deleted rows back (this is what usage counting relies on),
    // and the row keeps the time it was cleared.
    [Fact]
    public async Task IgnoreQueryFilters_returns_soft_deleted_scans()
    {
        using var database = new TestDatabase();
        var user = database.AddUser("alice");
        var created = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
        var clearedAt = new DateTime(2026, 10, 3, 9, 30, 0, DateTimeKind.Utc);
        database.AddScan(user.Id, created, "visible.example.com");
        var deleted = database.AddScan(user.Id, created, "deleted.example.com", deletedAtUtc: clearedAt);
        using var db = database.CreateContext();

        Assert.Equal(2, await db.Scans.IgnoreQueryFilters().CountAsync());
        var row = await db.Scans.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == deleted.Id);
        Assert.Equal(clearedAt, row.DeletedAt);
    }

    // Soft deleting through the same statement the API uses keeps the rows, hides them, and
    // leaves other users' scans alone.
    [Fact]
    public async Task Soft_delete_keeps_rows_hides_them_and_only_touches_that_user()
    {
        using var database = new TestDatabase();
        var alice = database.AddUser("alice");
        var bob = database.AddUser("bob");
        var created = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
        database.AddScan(alice.Id, created);
        database.AddScan(alice.Id, created.AddHours(1));
        database.AddScan(bob.Id, created);

        var cleared = database.SoftDeleteScans(alice.Id, created.AddDays(1));

        using var db = database.CreateContext();
        Assert.Equal(2, cleared);
        Assert.Equal(0, await db.Scans.CountAsync(s => s.UserId == alice.Id));
        Assert.Equal(2, await db.Scans.IgnoreQueryFilters().CountAsync(s => s.UserId == alice.Id));
        Assert.Equal(1, await db.Scans.CountAsync(s => s.UserId == bob.Id));
        Assert.Equal(0, database.SoftDeleteScans(alice.Id, created.AddDays(2))); // already cleared: nothing to do
    }
}
