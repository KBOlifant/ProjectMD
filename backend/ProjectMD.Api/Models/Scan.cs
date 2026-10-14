namespace ProjectMD.Api.Models;

// One saved scan of a website: the generated Markdown plus a little metadata. Owned by one AppUser.
public class Scan
{
    // Primary key; also the id the frontend uses.
    public Guid Id { get; set; }

    // The owner (foreign key to AppUser). Deleting the user deletes their scans.
    public Guid UserId { get; set; }

    // Navigation to the owner.
    public AppUser? User { get; set; }

    // The normalised URL that was scanned (max 2048 characters).
    public string Url { get; set; } = string.Empty;

    // The host shown in the UI, without "www." (max 255 characters).
    public string Host { get; set; } = string.Empty;

    // When the scan was created (UTC).
    public DateTime CreatedAt { get; set; }

    // How long the scan took, in milliseconds.
    public int DurationMs { get; set; }

    // Confidence score from 0 to 100.
    public int Confidence { get; set; }

    // Number of pages the scan covered.
    public int Pages { get; set; }

    // Detected technologies. Stored as a jsonb column on the Scan row (no separate table).
    public List<ScanTech> Tech { get; set; } = new();

    // The generated Markdown document.
    public string Markdown { get; set; } = string.Empty;

    // When the owner cleared their history (UTC, timestamptz), or null while the scan is visible.
    // Clearing is a SOFT delete: the row stays in the table so it keeps counting toward the monthly
    // quota (otherwise "create, clear, repeat" would refund the allowance). AppDbContext hides rows
    // where this is set from every normal query; only usage counting looks at them.
    public DateTime? DeletedAt { get; set; }
}
