using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Controllers;

// The signed-in user's scans: list, open one, create one, clear them all. Every query is filtered
// by the caller's own AppUser id, so nobody can see or delete another person's scans.
[ApiController]
[Authorize]
[Route("api/scans")]
public class ScansController : ControllerBase
{
    // The most Markdown (in UTF-8 bytes) we store for one scan: 1 MB.
    public const int MaxMarkdownBytes = 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserSync _userSync;
    private readonly IUrlValidator _urlValidator;
    private readonly IScanGenerator _generator;
    private readonly IUsageService _usage;
    private readonly IScanRecorder _recorder;
    private readonly TimeProvider _clock;
    private readonly ILogger<ScansController> _logger;

    // Receives everything the endpoints need from dependency injection.
    public ScansController(
        AppDbContext db,
        ICurrentUser currentUser,
        IUserSync userSync,
        IUrlValidator urlValidator,
        IScanGenerator generator,
        IUsageService usage,
        IScanRecorder recorder,
        TimeProvider clock,
        ILogger<ScansController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _userSync = userSync;
        _urlValidator = urlValidator;
        _generator = generator;
        _usage = usage;
        _recorder = recorder;
        _clock = clock;
        _logger = logger;
    }

    // GET /api/scans: the caller's scans, newest first.
    [HttpGet]
    [Authorize(Policy = Permissions.ScanRead)]
    public async Task<ActionResult<IReadOnlyList<ScanDto>>> List(CancellationToken ct)
    {
        var user = await _userSync.GetOrCreateAsync(_currentUser, ct);

        var scans = await _db.Scans
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return Ok(scans.Select(s => s.ToDto()).ToList());
    }

    // GET /api/scans/{id}: one of the caller's scans. A scan that does not exist and a scan that
    // belongs to someone else both give the same 404, so ids cannot be probed.
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.ScanRead)]
    public async Task<ActionResult<ScanDto>> GetById(Guid id, CancellationToken ct)
    {
        var user = await _userSync.GetOrCreateAsync(_currentUser, ct);

        var scan = await _db.Scans
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, ct);

        return scan is null ? NotFound() : Ok(scan.ToDto());
    }

    // POST /api/scans { url }: validates the URL, checks the monthly quota, generates the scan,
    // saves it and returns 201 with the scan and a Location header. Errors are ProblemDetails:
    // 400 (bad URL, reason "url"), 429 (quota used up, reason "quota"; too fast, reason "rate").
    [HttpPost]
    [Authorize(Policy = Permissions.ScanCreate)]
    [EnableRateLimiting(ScanRateLimiting.PolicyName)]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<ScanDto>> Create([FromBody] CreateScanRequest request, CancellationToken ct)
    {
        var validation = _urlValidator.Validate(request?.Url);
        if (!validation.IsValid)
        {
            var invalid = ProblemDetailsFactory.CreateProblemDetails(
                HttpContext,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid URL",
                detail: validation.Error);
            invalid.Extensions["reason"] = "url";
            return new ObjectResult(invalid) { StatusCode = StatusCodes.Status400BadRequest };
        }

        var user = await _userSync.GetOrCreateAsync(_currentUser, ct);

        // Early, cheap quota check so a user who is over the limit never triggers generation (which
        // will cost money once a real generator is plugged in). The authoritative check is the
        // transactional one in TryRecordAsync below, because requests can arrive at the same time.
        var usage = await _usage.GetAsync(user.Id, ct);
        if (usage.Used >= usage.Limit)
        {
            return QuotaExceeded(usage);
        }

        var generated = await _generator.GenerateAsync(validation.Uri!, ct);

        // Refuse to store an oversized document. The message is generic on purpose.
        if (Encoding.UTF8.GetByteCount(generated.Markdown) > MaxMarkdownBytes)
        {
            _logger.LogError(
                "Generated Markdown for host {Host} is larger than {MaxBytes} bytes and was not saved",
                validation.Host,
                MaxMarkdownBytes);
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The scan could not be saved",
                detail: "The generated document was too large to store. Please try again later.");
        }

        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Url = validation.Url!,
            Host = validation.Host!,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            DurationMs = Math.Max(0, generated.DurationMs),
            Confidence = Math.Clamp(generated.Confidence, 0, 100),
            Pages = Math.Max(0, generated.Pages),
            Tech = generated.Tech.Select(t => new ScanTech { Name = t.Name, Highlighted = t.Highlighted }).ToList(),
            Markdown = generated.Markdown,
        };

        if (!await _recorder.TryRecordAsync(scan, ct))
        {
            return QuotaExceeded(await _usage.GetAsync(user.Id, ct));
        }

        // Host only: the full URL may carry a query string with tokens, so it is never logged.
        _logger.LogInformation("Scan {ScanId} created for host {Host}", scan.Id, scan.Host);

        return CreatedAtAction(nameof(GetById), new { id = scan.Id }, scan.ToDto());
    }

    // DELETE /api/scans: deletes every scan the caller owns (and nobody else's). Returns 204.
    [HttpDelete]
    [Authorize(Policy = Permissions.HistoryClear)]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var user = await _userSync.GetOrCreateAsync(_currentUser, ct);

        var removed = await _db.Scans
            .Where(s => s.UserId == user.Id)
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation("Cleared {Count} scans for one user", removed);
        return NoContent();
    }

    // The 429 response for a used-up monthly allowance, with reason "quota" and the numbers the
    // frontend needs to explain it.
    private ObjectResult QuotaExceeded(UsageDto usage)
    {
        var resetsAt = usage.ResetsAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Monthly scan limit reached",
            detail: $"You have used {usage.Used} of {usage.Limit} scans this month. The allowance resets on {resetsAt[..10]}.");
        problem.Extensions["reason"] = "quota";
        problem.Extensions["used"] = usage.Used;
        problem.Extensions["limit"] = usage.Limit;
        problem.Extensions["resetsAt"] = resetsAt;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status429TooManyRequests };
    }
}
