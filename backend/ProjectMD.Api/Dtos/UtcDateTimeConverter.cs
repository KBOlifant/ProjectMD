using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectMD.Api.Dtos;

// Writes DateTime values as UTC ISO-8601 with milliseconds and a trailing Z, for example
// 2026-10-14T09:30:00.123Z. This is the same shape as JavaScript's Date.toISOString(), which is
// what the frontend expects. A DateTime of unspecified kind is treated as UTC.
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // Reads an ISO-8601 string and returns it as a UTC DateTime.
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("Expected an ISO-8601 date string.");
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime;
    }

    // Writes the value as a UTC ISO-8601 string.
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString(Format, CultureInfo.InvariantCulture));
    }
}
