using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;

namespace ProjectMD.Api.Services;

// The database-backed IUsageService: counts the user's scans created this UTC month. There is no
// monthly cap (see GetAsync).
public sealed class UsageService : IUsageService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    // Receives the database and the clock (replaceable in tests).
    public UsageService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    // used = scans with CreatedAt in the current UTC month; limit = Usage:MonthlyLimit (never
    // below 0); resetsAt = midnight UTC on the 1st of next month. Read-only, so no tracking.
    // IgnoreQueryFilters is deliberate and essential: scans the user cleared from their history
    // are soft deleted (DeletedAt set) and hidden by the global filter, but they were still
    // created this month, so they must still count. Without it, clearing history would refund
    // the allowance. Do not remove it, and do not add a DeletedAt condition here.
    public async Task<UsageDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        var used = await _db.Scans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && s.CreatedAt >= monthStart && s.CreatedAt < nextMonthStart, ct);

        // No monthly cap: limit is reported as unbounded so the quota checks in ScansController and
        // ScanRecorder (used >= limit) never trip. Usage:MonthlyLimit in config is no longer read.
        return new UsageDto(used, int.MaxValue, nextMonthStart);
    }
}
