using Microsoft.EntityFrameworkCore;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Data;

// The EF Core database context: the two tables (AppUsers, Scans) and how they map to PostgreSQL.
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

    // Describes the schema: key sizes, indexes, the cascade delete and the jsonb tech column.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("AppUsers");
            user.HasKey(u => u.Id);

            user.Property(u => u.FirebaseUid).IsRequired().HasMaxLength(128);
            user.Property(u => u.Email).HasMaxLength(320);

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
    }
}
