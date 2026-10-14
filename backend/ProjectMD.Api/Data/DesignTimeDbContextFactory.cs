using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ProjectMD.Api.Data;

// Used only by `dotnet ef` (migrations). It reads the same configuration as the app, so the
// connection string comes from user-secrets or the ConnectionStrings__Default environment
// variable. Generating a migration does not open a connection, so a dummy local value works.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Builds a context for the EF tools without starting the web host.
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not set. Set it with user-secrets or the " +
                "ConnectionStrings__Default environment variable (a dummy local value is fine " +
                "for generating migrations; see backend/README.md).");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options);
    }
}
