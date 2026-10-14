using System.Text.Json.Serialization;
using ProjectMD.Api.Models;

namespace ProjectMD.Api.Dtos;

// A scan as the frontend receives it (matches frontend/lib/types.ts). JSON is camelCase:
// { id, url, host, createdAt, durationMs, confidence, pages, tech: [{ name, highlighted }], markdown }.
// Id is a string (the GUID) and createdAt is a UTC ISO-8601 string.
public record ScanDto(
    string Id,
    string Url,
    string Host,
    [property: JsonConverter(typeof(UtcDateTimeConverter))] DateTime CreatedAt,
    int DurationMs,
    int Confidence,
    int Pages,
    IReadOnlyList<TechDto> Tech,
    string Markdown);

// Small mapping helpers from database models to DTOs.
public static class ScanMappingExtensions
{
    // Converts a stored Scan into the DTO sent to the frontend.
    public static ScanDto ToDto(this Scan scan) => new(
        scan.Id.ToString(),
        scan.Url,
        scan.Host,
        scan.CreatedAt,
        scan.DurationMs,
        scan.Confidence,
        scan.Pages,
        scan.Tech.Select(t => t.ToDto()).ToList(),
        scan.Markdown);

    // Converts a stored ScanTech into its DTO.
    public static TechDto ToDto(this ScanTech tech) => new(tech.Name, tech.Highlighted);
}
