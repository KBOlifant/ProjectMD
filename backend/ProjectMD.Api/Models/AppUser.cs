namespace ProjectMD.Api.Models;

// One row per signed-in person, keyed by their Firebase uid. Created on first call to the API.
// The role lives here and is decided by the server only; a role claim in the token is ignored.
public class AppUser
{
    // Our own primary key (a random GUID), never exposed as the user's identity.
    public Guid Id { get; set; }

    // The Firebase uid (the token's `sub`). Unique, so one Firebase account maps to one row.
    public string FirebaseUid { get; set; } = string.Empty;

    // The email from the token at last sign-in. Can be missing for some providers.
    public string? Email { get; set; }

    // "viewer", "member" or "admin" (column max 20, default "member"). Anything else stored here is
    // treated as "viewer" when read (fail closed).
    public string Role { get; set; } = "member";

    // When this row was first created (UTC).
    public DateTime CreatedAt { get; set; }

    // The last time this person called the API (UTC).
    public DateTime LastSeenAt { get; set; }

    // Navigation: the scans this person has made.
    public List<Scan> Scans { get; set; } = new();
}
