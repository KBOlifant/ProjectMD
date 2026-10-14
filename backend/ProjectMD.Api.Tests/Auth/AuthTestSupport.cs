using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Tests.Auth;

// Builds ClaimsPrincipals that look like a verified Firebase token, for policy tests that do not
// need a real HTTP request.
public static class TestPrincipal
{
    // A signed-in principal with the given claims. `role` null leaves the claim out. Set
    // `authenticated` to false for an anonymous caller.
    public static ClaimsPrincipal Create(
        string? role = null,
        bool? emailVerified = true,
        string uid = "uid-1",
        string? email = "person@example.com",
        bool authenticated = true,
        IEnumerable<Claim>? extra = null)
    {
        var claims = new List<Claim> { new(FirebaseClaims.Subject, uid) };
        if (email is not null)
        {
            claims.Add(new Claim(FirebaseClaims.Email, email));
        }

        if (emailVerified is not null)
        {
            claims.Add(new Claim(FirebaseClaims.EmailVerified, emailVerified.Value ? "true" : "false", ClaimValueTypes.Boolean));
        }

        if (role is not null)
        {
            claims.Add(new Claim(FirebaseClaims.Role, role));
        }

        if (extra is not null)
        {
            claims.AddRange(extra);
        }

        return new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity(claims));
    }
}

// A throwaway in-memory SQLite database with the real schema. Never a real database. The shared
// connection keeps the data alive for as long as this object is.
public sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    // Opens the connection and creates the tables from the EF model.
    public SqliteTestDb()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    // A new context on this database (optionally with extra interceptors).
    public AppDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;
        return new AppDbContext(options);
    }

    // Closes the database.
    public void Dispose() => _connection.Dispose();
}

// A usage calculator that always reports the same numbers, so /api/me can be tested without the
// scans work. Records which user id it was asked about.
public sealed class AuthFakeUsageService : IUsageService
{
    // The user id of the last call.
    public static Guid? LastUserId { get; private set; }

    // Returns 3 of 10 used, resetting at the start of next year.
    public Task<UsageDto> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        LastUserId = userId;
        return Task.FromResult(new UsageDto(3, 10, new DateTime(DateTime.UtcNow.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}

// Endpoints that exist only in the test host, one per policy, so the policies and the 401/403
// bodies can be tested end to end without depending on the scans controller.
[ApiController]
[Route("api/_authprobe")]
public class AuthProbeController : ControllerBase
{
    // Needs scan:create.
    [HttpGet("scan-create")]
    [Authorize(Policy = Permissions.ScanCreate)]
    public IActionResult ScanCreate() => Ok();

    // Needs scan:read.
    [HttpGet("scan-read")]
    [Authorize(Policy = Permissions.ScanRead)]
    public IActionResult ScanRead() => Ok();

    // Needs scan:export.
    [HttpGet("scan-export")]
    [Authorize(Policy = Permissions.ScanExport)]
    public IActionResult ScanExport() => Ok();

    // Needs history:clear.
    [HttpGet("history-clear")]
    [Authorize(Policy = Permissions.HistoryClear)]
    public IActionResult HistoryClear() => Ok();

    // Needs admin:access.
    [HttpGet("admin-access")]
    [Authorize(Policy = Permissions.AdminAccess)]
    public IActionResult AdminAccess() => Ok();

    // No attribute at all: protected only by the fallback policy.
    [HttpGet("no-attribute")]
    public IActionResult NoAttribute() => Ok();

    // Explicitly public.
    [HttpGet("anonymous")]
    [AllowAnonymous]
    public IActionResult Anonymous() => Ok();
}

// Collects every log line (message and exception text) so tests can prove no token is logged.
public sealed class LogSink : ILoggerProvider
{
    private readonly List<string> _lines = new();

    // Everything logged so far.
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToList();
            }
        }
    }

    // Makes a logger that writes into this sink.
    public ILogger CreateLogger(string categoryName) => new SinkLogger(this);

    public void Dispose()
    {
    }

    // Stores one line.
    private void Add(string line)
    {
        lock (_lines)
        {
            _lines.Add(line);
        }
    }

    // The logger handed out by the sink.
    private sealed class SinkLogger(LogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink.Add(formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception));
        }
    }
}

// The real API host (Program.cs) in the "Testing" environment, with: an in-memory SQLite
// database, the test signing key trusted instead of Google's, a fake usage service, the probe
// controller and a log sink. The real authentication and authorization setup is what runs.
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    // Every log line written by the host.
    public LogSink Logs { get; } = new();

    // Configures the host described above.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Firebase:ProjectId", TestFirebase.ProjectId);

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);

            // Capture everything, including what appsettings.json would normally filter out, so
            // the "no tokens in logs" test sees the most verbose output possible.
            logging.AddFilter("Microsoft", LogLevel.Trace);
            logging.AddFilter("Microsoft.AspNetCore", LogLevel.Trace);
            logging.AddFilter("System", LogLevel.Trace);
            logging.AddProvider(Logs);
        });

        builder.ConfigureTestServices(services =>
        {
            _connection.Open();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IUsageService>();
            services.AddScoped<IUsageService, AuthFakeUsageService>();

            // Trust the test key instead of downloading Firebase's keys.
            var configuration = new OpenIdConnectConfiguration { Issuer = TestFirebase.Issuer };
            configuration.SigningKeys.Add(TestTokens.SigningKey);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly);
        });
    }

    // Creates the tables once the host exists.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return host;
    }

    // Closes the database with the host.
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
