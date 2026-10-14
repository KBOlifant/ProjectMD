namespace ProjectMD.Api.Dtos;

// The body of POST /api/scans: { url }. Url is nullable on purpose so a missing field reaches the
// URL validator and gets the same friendly message as an empty box, not a generic model error.
public record CreateScanRequest(string? Url = null);
