namespace ProjectMD.Api.Models;

// One row per signed-in person, keyed by their Firebase uid. Created on first call to the API.
// Roles are NOT stored here: the Firebase custom claim is the source of truth.
public class AppUser
{
    // Our own primary key (a random GUID), never exposed as the user's identity.
    public Guid Id { get; set; }

    // The Firebase uid (the token's `sub`). Unique, so one Firebase account maps to one row.
    public string FirebaseUid { get; set; } = string.Empty;

    // The email from the token at last sign-in. Can be missing for some providers.
    public string? Email { get; set; }

    // When this row was first created (UTC).
    public DateTime CreatedAt { get; set; }

    // The last time this person called the API (UTC).
    public DateTime LastSeenAt { get; set; }

    // Navigation: the scans this person has made.
    public List<Scan> Scans { get; set; } = new();
}
