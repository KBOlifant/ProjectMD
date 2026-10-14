using System.Text.Json.Serialization;
using ProjectMD.Api.Auth;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Dtos;

// A user as the admin screen sees it: { id, email, role, createdAt, lastSeenAt }. Id is the GUID as
// text, timestamps are UTC ISO-8601 strings. The Firebase uid is not exposed.
public record AdminUserDto(
    string Id,
    string? Email,
    string Role,
    [property: JsonConverter(typeof(UtcDateTimeConverter))] DateTime CreatedAt,
    [property: JsonConverter(typeof(UtcDateTimeConverter))] DateTime LastSeenAt)
{
    // Converts a stored user into the DTO (a corrupt stored role is shown as "viewer").
    public static AdminUserDto From(AppUser user) => new(
        user.Id.ToString(),
        user.Email,
        Roles.FromStored(user.Role),
        user.CreatedAt,
        user.LastSeenAt);
}
