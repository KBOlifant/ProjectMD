using System.Text.Json.Serialization;

namespace ProjectMD.Api.Dtos;

// This month's scan usage: { used, limit, resetsAt }. ResetsAt is the first instant of next month
// (UTC) and is sent as an ISO-8601 string.
public record UsageDto(
    int Used,
    int Limit,
    [property: JsonConverter(typeof(UtcDateTimeConverter))] DateTime ResetsAt);
