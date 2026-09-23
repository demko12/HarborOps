using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HarborOps.Modules.Voyages.Contracts;

public sealed partial class ExplicitOffsetDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("The value must be an ISO 8601 timestamp string with an explicit UTC offset.");
        }

        var value = reader.GetString();

        if (value is null
            || !ExplicitOffsetPattern().IsMatch(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new JsonException(
                "The value must be an ISO 8601 timestamp with an explicit UTC offset, for example 2026-09-21T08:30:00Z or 2026-09-21T10:30:00+02:00.");
        }

        return parsed;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex ExplicitOffsetPattern();
}
