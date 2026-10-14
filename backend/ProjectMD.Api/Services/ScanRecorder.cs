using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Services;

// Saves a new scan only if the user still has monthly allowance left.
public interface IScanRecorder
{
    // Checks the quota and inserts the scan in one transaction. Returns false (and saves nothing)
    // when the user has already used up this month's allowance.
    Task<bool> TryRecordAsync(Scan scan, CancellationToken ct);
}

// The database-backed IScanRecorder.
public sealed class ScanRecorder : IScanRecorder
{
    private readonly AppDbContext _db;
    private readonly IUsageService _usage;
    private readonly TimeProvider _clock;

    // Receives the database, the usage counter and the clock.
    public ScanRecorder(AppDbContext db, IUsageService usage, TimeProvider clock)
    {
        _db = db;
        _usage = usage;
        _clock = clock;
    }

    // Quota check and insert happen in ONE transaction, serialised per user, so two requests that
    // arrive together cannot both slip past the limit. How: the first statement updates the
    // user's own AppUsers row (LastSeenAt). The database holds that row lock until commit, so a
    // second transaction for the same user waits at that statement, and when it continues it
    // counts a table that already contains the first scan. Other users are not blocked. This
    // works on PostgreSQL's default isolation level and also on SQLite (used in tests), unlike
    // SELECT ... FOR UPDATE or a serializable transaction that would need retries on conflict.
    // The count comes from IUsageService, which also counts scans the user cleared from their
    // history (soft deleted, DeletedAt set), so clearing history can never free up allowance. It
    // also means a "clear history" running at the same moment cannot change the number this
    // transaction sees: clearing only sets DeletedAt and never removes a row from the count.
    public async Task<bool> TryRecordAsync(Scan scan, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var now = _clock.GetUtcNow().UtcDateTime;
        var locked = await _db.AppUsers
            .Where(u => u.Id == scan.UserId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.LastSeenAt, now), ct);
        if (locked == 0)
        {
            throw new InvalidOperationException("Cannot record a scan for a user that does not exist.");
        }

        var usage = await _usage.GetAsync(scan.UserId, ct);
        if (usage.Used >= usage.Limit)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        _db.Scans.Add(scan);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
