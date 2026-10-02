using System.Text;
using HarborOps.Modules.Fleet.Application;

namespace HarborOps.UnitTests.Fleet;

public sealed class AisMessageProcessorTests
{
    private readonly AisTrackingState state = new();

    [Fact]
    public void Missing_mmsi_waits_for_registry_configuration()
    {
        var processor = CreateProcessor();
        Process(processor, Position());
        Assert.Empty(state.Snapshot());
        processor.SetFleet([("9228801", "227441980")]);
        Process(processor, Position());
        var position = state.Snapshot()["9228801"];
        Assert.Equal(42.1m, position.Latitude);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), position.ReportedAtUtc);
    }

    [Fact]
    public void Registered_mmsi_does_not_need_a_static_message()
    {
        var processor = CreateProcessor("227441980");
        Process(processor, Position());
        Assert.Single(state.Snapshot());
        Assert.Equal(["227441980"], processor.TrackedMmsis);
        Assert.Equal("Connected", state.Status.ConnectionState);
    }

    [Fact]
    public void Latest_observation_time_does_not_regress_across_vessels()
    {
        var latest = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        state.Record("9228801", new AisPosition(42, 10, latest));
        state.Record("9228802", new AisPosition(43, 11, latest.AddHours(-1)));
        Assert.Equal(latest, state.Status.LastPositionReceivedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("abcdefghi")]
    [InlineData("000000000")]
    public void Invalid_registry_mmsis_are_excluded_from_subscription(string? mmsi)
    {
        Assert.Empty(CreateProcessor(mmsi).TrackedMmsis);
    }

    [Theory]
    [InlineData(91, 10, true, "2026-10-01 10:00:00 +0000 UTC")]
    [InlineData(42, 181, true, "2026-10-01 10:00:00 +0000 UTC")]
    [InlineData(42, 10, false, "2026-10-01 10:00:00 +0000 UTC")]
    [InlineData(42, 10, true, "invalid")]
    [InlineData(42, 10, true, "2027-10-01T10:00:00Z")]
    public void Invalid_or_unavailable_positions_are_ignored(decimal latitude, decimal longitude, bool valid, string timestamp)
    {
        var processor = CreateProcessor("227441980");
        Process(processor, Position(latitude, longitude, valid, timestamp));
        Assert.Empty(state.Snapshot());
    }

    [Fact]
    public void Unrelated_vessels_and_replaced_registry_mmsi_are_ignored()
    {
        var processor = CreateProcessor();
        Process(processor, Position());
        Assert.Empty(state.Snapshot());
        processor.SetFleet([("9228801", "227441981")]);
        Process(processor, Position());
        Assert.Empty(state.Snapshot());
    }

    [Fact]
    public void Older_observations_cannot_replace_newer_positions_and_removed_vessels_are_evicted()
    {
        var processor = CreateProcessor("227441980");
        Process(processor, Position());
        Process(processor, Position(10, 20, true, "2026-09-30T10:00:00Z"));
        Assert.Equal(42.1m, state.Snapshot()["9228801"].Latitude);
        processor.SetFleet([]);
        Assert.Empty(state.Snapshot());
    }

    [Fact]
    public void Confirmation_sets_connection_status_and_provider_errors_do_not_expose_payload()
    {
        var processor = CreateProcessor();
        Process(processor, """{"MessageType":"SubscriptionConfirmation","Message":{"CompressionEnabled":true}}""");
        Assert.Equal("Connected", state.Status.ConnectionState);
        var exception = Assert.Throws<InvalidOperationException>(() => Process(processor, """{"error":"secret key rejected"}"""));
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    private AisMessageProcessor CreateProcessor(string? mmsi = null)
    {
        var processor = new AisMessageProcessor(state, new FixedTimeProvider());
        processor.SetFleet([("9228801", mmsi)]);
        return processor;
    }

    private static void Process(AisMessageProcessor processor, string message) => processor.Process(Encoding.UTF8.GetBytes(message));

    private static string Position(decimal latitude = 42.1m, decimal longitude = 10.2m, bool valid = true,
        string timestamp = "2026-10-01 10:00:00 +0000 UTC") => System.Text.Json.JsonSerializer.Serialize(new
        {
            MessageType = "PositionReport",
            MetaData = new { time_utc = timestamp },
            Message = new { PositionReport = new { Valid = valid, UserID = 227441980, Latitude = latitude, Longitude = longitude } },
        });

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
