using System.Text.Json;
using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.UnitTests.Voyages;

public sealed class ExplicitOffsetDateTimeOffsetJsonConverterTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("2026-09-21T08:30:00Z", 0)]
    [InlineData("2026-09-21T10:30:00+02:00", 2)]
    [InlineData("2026-09-21T03:30:00.1234567-05:00", -5)]
    [InlineData("2026-09-21T10:30+02:00", 2)]
    public void A_timestamp_with_an_explicit_offset_is_accepted_and_keeps_its_offset(
        string value,
        int expectedOffsetHours)
    {
        var request = Deserialize($$"""{"activityType":"AllFast","occurredAt":"{{value}}"}""");

        Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), request.OccurredAt!.Value.Offset);
        Assert.Equal(new DateTime(2026, 9, 21, 8, 30, 0, DateTimeKind.Utc), TruncateToMinute(request.OccurredAt.Value.UtcDateTime));
    }

    [Theory]
    [InlineData("2026-09-21T08:30:00")]
    [InlineData("2026-09-21")]
    [InlineData("21.09.2026 08:30")]
    [InlineData("2026-09-21T08:30:00+0200")]
    [InlineData("")]
    public void A_timestamp_without_an_explicit_offset_is_rejected(string value)
    {
        var exception = Assert.Throws<JsonException>(() =>
            Deserialize($$"""{"activityType":"AllFast","occurredAt":"{{value}}"}"""));

        Assert.Contains("explicit UTC offset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_numeric_timestamp_is_rejected()
    {
        Assert.Throws<JsonException>(() =>
            Deserialize("""{"activityType":"AllFast","occurredAt":1758443400}"""));
    }

    [Fact]
    public void Null_and_missing_values_stay_empty_so_the_validator_can_report_them()
    {
        var explicitNull = Deserialize("""{"activityType":"AllFast","occurredAt":null,"endedAt":null}""");
        var missing = Deserialize("""{"activityType":"AllFast"}""");

        Assert.Null(explicitNull.OccurredAt);
        Assert.Null(explicitNull.EndedAt);
        Assert.Null(missing.OccurredAt);
    }

    [Fact]
    public void Serializing_writes_a_value_that_reads_back_with_the_same_offset()
    {
        var original = new DateTimeOffset(2026, 9, 21, 10, 30, 0, TimeSpan.FromHours(2));

        var json = JsonSerializer.Serialize(
            new RecordPortCallActivityRequest { OccurredAt = original },
            WebOptions);
        var roundTripped = Deserialize(json);

        Assert.Equal(original, roundTripped.OccurredAt);
        Assert.Equal(original.Offset, roundTripped.OccurredAt!.Value.Offset);
    }

    private static RecordPortCallActivityRequest Deserialize(string json) =>
        JsonSerializer.Deserialize<RecordPortCallActivityRequest>(json, WebOptions)!;

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
}
