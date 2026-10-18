using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;

namespace ProjectMD.Api.Data;

// The EF Core database context: the tables (AppUsers, Scans, ExtractionCache, LlmUsage) and how
// they map to PostgreSQL.
// All DateTime values are UTC and stored as timestamptz; always write DateTime.UtcNow.
public class AppDbContext : DbContext
{
    // Receives the options (provider and connection string) registered in Program.cs.
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // People who have signed in at least once.
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    // Saved scans, each owned by one AppUser.
    public DbSet<Scan> Scans => Set<Scan>();

    // Cached AI prose per page content (table "ExtractionCache"), so identical work is not paid for twice.
    public DbSet<ExtractionCacheEntry> ExtractionCache => Set<ExtractionCacheEntry>();

    // The AI usage ledger (table "LlmUsage"): token counts per call, used for the daily caps.
    public DbSet<LlmUsageRecord> LlmUsage => Set<LlmUsageRecord>();

    // Describes the schema: key sizes, indexes, the cascade delete, the soft-delete filter on Scans
    // and the jsonb tech column.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("AppUsers");
            user.HasKey(u => u.Id);

            user.Property(u => u.FirebaseUid).IsRequired().HasMaxLength(128);
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Role).IsRequired().HasMaxLength(20).HasDefaultValue("member");

            // DateTime maps to "timestamp with time zone" (timestamptz) in Npgsql, so every
            // timestamp is stored as UTC. No explicit column type is set, to keep the model
            // provider-neutral for tests.

            // One Firebase account maps to exactly one row; also the lookup key for every request.
            user.HasIndex(u => u.FirebaseUid).IsUnique();
        });

        modelBuilder.Entity<Scan>(scan =>
        {
            scan.ToTable("Scans");
            scan.HasKey(s => s.Id);

            scan.Property(s => s.Url).IsRequired().HasMaxLength(2048);
            scan.Property(s => s.Host).IsRequired().HasMaxLength(255);
            scan.Property(s => s.Markdown).IsRequired(); // unbounded "text" column

            // Deleting a user deletes all of their scans.
            scan.HasOne(s => s.User)
                .WithMany(u => u.Scans)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Clearing history is a soft delete (DeletedAt is set, the row stays). This global filter
            // hides cleared scans from every query on Scans (list, get by id, anything else), so a
            // cleared scan can never leak back out. The one caller that must still see them is the
            // monthly usage count (UsageService), which uses IgnoreQueryFilters so a cleared scan
            // keeps using up the allowance for the month it was created in.
            scan.HasQueryFilter(s => s.DeletedAt == null);

            // The captured design/API data is one jsonb column holding the camelCase JSON text.
            scan.Property(s => s.Analysis)
                .HasColumnType("jsonb")
                .HasConversion(
                    a => a == null ? null : ScanAnalysisJson.Serialize(a),
                    json => ScanAnalysisJson.Deserialize(json));

            // Serves "my scans, newest first" and the monthly quota count.
            scan.HasIndex(s => new { s.UserId, s.CreatedAt })
                .IsDescending(false, true);

            // The technologies are stored as a jsonb array inside the Scan row, not in their own table.
            scan.OwnsMany(s => s.Tech, tech =>
            {
                tech.ToJson();
                tech.Property(t => t.Name).IsRequired();
            });
        });

        // AI cache: one row per (page content hash, prompt version, model). Holds the AI-written
        // prose only; never the page HTML.
        modelBuilder.Entity<ExtractionCacheEntry>(entry =>
        {
            entry.ToTable("ExtractionCache");
            entry.HasKey(e => new { e.ContentHash, e.PromptVersion, e.Model });

            entry.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
            entry.Property(e => e.Model).IsRequired().HasMaxLength(100);
            entry.Property(e => e.Markdown).IsRequired(); // unbounded "text" column
        });

        // AI usage ledger: one row per model call or cache hit. Deleting a user deletes their rows.
        modelBuilder.Entity<LlmUsageRecord>(usage =>
        {
            usage.ToTable("LlmUsage");
            usage.HasKey(u => u.Id);

            usage.Property(u => u.Model).IsRequired().HasMaxLength(100);

            usage.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(u => u.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves "how many tokens has this user spent today".
            usage.HasIndex(u => new { u.UserId, u.CreatedAt });
        });
    }
}
