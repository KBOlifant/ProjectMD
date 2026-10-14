using ProjectMD.Api.Dtos;

namespace ProjectMD.Api.Services;

// Reads how much of the monthly scan allowance a user has used.
public interface IUsageService
{
    // This month's usage for the user: scans created in the current UTC calendar month (including
    // ones the user cleared from their history, which are only soft deleted), the monthly limit,
    // and the first instant of next month (UTC) when the count starts again.
    Task<UsageDto> GetAsync(Guid userId, CancellationToken ct);
}
