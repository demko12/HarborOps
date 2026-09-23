using System.Text.Json.Serialization;

namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordPortCallActivityRequest
{
    public string? ActivityType { get; init; }

    [JsonConverter(typeof(ExplicitOffsetDateTimeOffsetJsonConverter))]
    public DateTimeOffset? OccurredAt { get; init; }

    [JsonConverter(typeof(ExplicitOffsetDateTimeOffsetJsonConverter))]
    public DateTimeOffset? EndedAt { get; init; }

    public string? Notes { get; init; }
}
