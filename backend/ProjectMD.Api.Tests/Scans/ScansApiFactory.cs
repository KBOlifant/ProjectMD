using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
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
using ProjectMD.Api.Data;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;
using ProjectMD.Api.Tests.Auth;

namespace ProjectMD.Api.Tests.Scans;

// Collects log lines (message plus exception text) so a test can prove something was not logged.
public sealed class CapturingLoggerProvider : ILoggerProvider
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

    // Makes a logger that writes into this provider.
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    // Stores one log line.
    private void Add(string line)
    {
        lock (_lines)
        {
            _lines.Add(line);
        }
    }

    // The logger handed out by the provider.
    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;

        // Remembers the provider to write to.
        public CapturingLogger(CapturingLoggerProvider owner)
        {
            _owner = owner;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _owner.Add(formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception));
        }
    }
}

// The real API host (Program.cs) in the "Testing" environment: an in-memory SQLite database
// instead of PostgreSQL, the test signing key trusted instead of Google's (tokens come from
// TestTokens), and the per-minute rate limit and clock under test control. The real
// authentication, authorization, rate limiter and scans controller all run.
public class ScansApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly int _permitsPerMinute;
    private readonly TimeProvider? _clock;
    private readonly Action<IServiceCollection>? _configure;

    // Every log line written by the host.
    public CapturingLoggerProvider Logs { get; } = new();

    // A host with a chosen rate limit, an optional fixed clock and an optional hook to replace
    // services. `monthlyLimit` is accepted for source compatibility with existing call sites but
    // ignored: there is no monthly cap any more (see UsageService).
    public ScansApiFactory(int monthlyLimit, int permitsPerMinute, TimeProvider? clock = null, Action<IServiceCollection>? configure = null)
    {
        _ = monthlyLimit;
        _permitsPerMinute = permitsPerMinute;
        _clock = clock;
        _configure = configure;
    }

    // Configures the host described above.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Firebase:ProjectId", TestFirebase.ProjectId);

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });

        builder.ConfigureTestServices(services =>
        {
            _connection.Open();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // Trust the test key instead of downloading Firebase's keys.
            var configuration = new OpenIdConnectConfiguration { Issuer = TestFirebase.Issuer };
            configuration.SigningKeys.Add(TestTokens.SigningKey);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.Configure<ScanRateLimitOptions>(options => options.PermitsPerMinute = _permitsPerMinute);

            if (_clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_clock);
            }

            _configure?.Invoke(services);
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

    // A client that sends a valid Firebase-style bearer token for the given person.
    public HttpClient CreateClientFor(string uid, bool emailVerified = true, string? role = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // The role lives in the database, so a requested role is stored there (the token carries none).
        if (role is not null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.AppUsers.FirstOrDefault(u => u.FirebaseUid == uid);
            if (user is null)
            {
                db.AppUsers.Add(new AppUser
                {
                    Id = Guid.NewGuid(),
                    FirebaseUid = uid,
                    Email = uid + "@example.com",
                    Role = role,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                });
            }
            else
            {
                user.Role = role;
            }

            db.SaveChanges();
        }

        var token = TestTokens.Create(uid: uid, email: uid + "@example.com", emailVerified: emailVerified);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // A client with no Authorization header.
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Writes a scan straight into the database for the person with that Firebase uid (creating the
    // person if needed) and returns the scan's id. Pass deletedAtUtc to seed a scan the person has
    // already cleared from their history (soft deleted).
    public async Task<Guid> SeedScanAsync(string uid, DateTime createdAtUtc, string host = "example.com", DateTime? deletedAtUtc = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.FirebaseUid == uid);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                FirebaseUid = uid,
                Email = uid + "@example.com",
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            };
            db.AppUsers.Add(user);
        }

        var scan = TestDatabase.NewScan(user.Id, createdAtUtc, host, deletedAtUtc);
        db.Scans.Add(scan);
        await db.SaveChangesAsync();
        return scan.Id;
    }

    // How many VISIBLE scans (not cleared from history) the person with that Firebase uid has in
    // the database right now. The global query filter hides soft-deleted rows from this count.
    public async Task<int> CountScansAsync(string uid)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Scans.CountAsync(s => s.User!.FirebaseUid == uid);
    }

    // How many scan ROWS the person has, including ones cleared from history (soft deleted).
    public async Task<int> CountAllScansAsync(string uid)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Scans.IgnoreQueryFilters().CountAsync(s => s.User!.FirebaseUid == uid);
    }

    // The DeletedAt value of every scan row the person has (null for scans still visible), including
    // cleared ones.
    public async Task<List<DateTime?>> GetDeletedAtValuesAsync(string uid)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Scans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.User!.FirebaseUid == uid)
            .Select(s => s.DeletedAt)
            .ToListAsync();
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


// The shared host for test classes: a big monthly limit and rate limit so ordinary tests are never
// throttled. (xUnit class fixtures need a parameterless constructor.)
public sealed class DefaultScansApiFactory : ScansApiFactory
{
    // Creates the host with generous limits.
    public DefaultScansApiFactory()
        : base(1000, 1000)
    {
    }
}