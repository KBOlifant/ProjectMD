namespace ProjectMD.Api.Models;

// One detected technology on a scan (for example "Next.js"). Stored inside the Scan's jsonb column.
public class ScanTech
{
    // The technology's display name.
    public string Name { get; set; } = string.Empty;

    // True when the UI should highlight this technology.
    public bool Highlighted { get; set; }
}
