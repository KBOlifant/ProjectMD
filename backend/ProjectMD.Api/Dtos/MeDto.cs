namespace ProjectMD.Api.Dtos;

// The response of GET /api/me: { uid, email, emailVerified, role, usage }. Role is one of
// "viewer", "member" or "admin".
public record MeDto(
    string Uid,
    string? Email,
    bool EmailVerified,
    string Role,
    UsageDto Usage);
