using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;

namespace ProjectMD.Api.Services;

// Settings for the monthly scan allowance, bound from the "Usage" section of configuration.
public sealed class UsageOptions
{
    // Scans a user may create per UTC calendar month (Usage:MonthlyLimit, default 10).
    public int MonthlyLimit { get; set; } = 10;
}

// The database-backed IUsageService: counts the user's scans created this UTC month.
public sealed class UsageService : IUsageService
{
    private readonly AppDbContext _db;
    private readonly IOptions<UsageOptions> _options;
    private readonly TimeProvider _clock;

    // Receives the database, the configured limit and the clock (replaceable in tests).
    public UsageService(AppDbContext db, IOptions<UsageOptions> options, TimeProvider clock)
    {
        _db = db;
        _options = options;
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

        return new UsageDto(used, Math.Max(0, _options.Value.MonthlyLimit), nextMonthStart);
    }
}
