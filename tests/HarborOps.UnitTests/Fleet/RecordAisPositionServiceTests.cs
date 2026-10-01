using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class RecordAisPositionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Received_position_survives_a_new_context_and_empty_ais_cache_and_is_visible_in_history_and_register()
    {
        var name = Guid.NewGuid().ToString();
        Guid vesselId;
        await using (var database = Database(name))
        {
            vesselId = (await SeedAsync(database)).Id;
            var processor = new AisMessageProcessor(new AisTrackingState(), new Clock());
            processor.SetFleet([("1000001", "227441980")]);
            var observation = processor.Process(System.Text.Encoding.UTF8.GetBytes("""
                {"MessageType":"PositionReport","MetaData":{"time_utc":"2026-10-01T11:59:00Z"},"Message":{"PositionReport":{"Valid":true,"UserID":227441980,"Latitude":42.123456,"Longitude":10.654321}}}
                """));
            await new RecordAisPositionService(database).RecordAsync(observation!, CancellationToken.None);
        }

        await using var reloaded = Database(name);
        var positions = await new VesselPositionsQuery(reloaded, new Clock(), new AisTrackingState())
            .GetAsync(new VesselPositionsRequest(), CancellationToken.None);
        var item = Assert.Single(positions.Items);
        Assert.Equal(42.123456m, item.CurrentLatitude);
        Assert.Equal(10.654321m, item.CurrentLongitude);
        Assert.Equal(Now.AddMinutes(-1).UtcDateTime, item.LastPositionReportedAtUtc);
        Assert.False(item.IsPositionStale);

        var history = await new VesselPositionHistoryQuery(reloaded, new Clock())
            .GetAsync(vesselId, new VesselPositionHistoryRequest(), CancellationToken.None);
        Assert.Equal(item.CurrentLatitude, Assert.Single(history.Items).Latitude);
        Assert.Equal(item.LastPositionReportedAtUtc, history.Items[0].ReportedAtUtc);
        var register = await new SearchVesselsQuery(reloaded).SearchAsync(new SearchVesselsRequest(), CancellationToken.None);
        Assert.Equal(item.CurrentLatitude, Assert.Single(register.Items).CurrentLatitude);
        Assert.Equal(item.LastPositionReportedAtUtc, register.Items[0].LastPositionReportedAtUtc);
    }

    [Fact]
    public async Task Replayed_and_older_observations_do_not_replace_newer_positions_or_duplicate_history()
    {
        await using var database = Database(Guid.NewGuid().ToString());
        var vessel = await SeedAsync(database);
        var service = new RecordAisPositionService(database);
        var observation = Observation(Now.AddMinutes(-2).UtcDateTime);
        await service.RecordAsync(observation, CancellationToken.None);
        await service.RecordAsync(observation, CancellationToken.None);
        await service.RecordAsync(Observation(Now.AddHours(-1).UtcDateTime), CancellationToken.None);
        Assert.Single(database.Set<VesselPositionReport>());

        await new RecordVesselPositionService(database, new Clock()).RecordAsync(vessel.Id,
            new RecordVesselPositionRequest { Latitude = 1, Longitude = 2 }, Guid.NewGuid(), CancellationToken.None);
        await service.RecordAsync(Observation(Now.AddMinutes(-1).UtcDateTime), CancellationToken.None);
        Assert.Equal(1m, vessel.CurrentLatitude);
        Assert.Equal(2, database.Set<VesselPositionReport>().Count());

        await service.RecordAsync(Observation(Now.AddSeconds(1).UtcDateTime), CancellationToken.None);
        Assert.Equal(42m, vessel.CurrentLatitude);
        Assert.Equal(new long[] { 1, 2, 3 }, database.Set<VesselPositionReport>().OrderBy(x => x.Sequence).Select(x => x.Sequence).ToArray());
    }

    [Fact]
    public async Task Registry_mmsi_changed_after_subscription_does_not_write_a_position()
    {
        await using var database = Database(Guid.NewGuid().ToString());
        await SeedAsync(database);
        var observation = Observation(Now.UtcDateTime) with { Mmsi = "227441981" };
        await new RecordAisPositionService(database).RecordAsync(observation, CancellationToken.None);
        Assert.Empty(database.Set<VesselPositionReport>());
        Assert.Null(Assert.Single(database.Set<Vessel>()).CurrentLatitude);
    }

    private static AisObservation Observation(DateTime timestamp) => new("1000001", "227441980", new AisPosition(42, 10, timestamp));

    private static async Task<Vessel> SeedAsync(HarborOpsDbContext database)
    {
        var type = VesselType.Register("Container ship", "CNTR", "Container ship");
        database.Add(type);
        var vessel = Vessel.Register(Guid.NewGuid(), type.Id, "North Star", "1000001", "Malta", "Active");
        vessel.UpdateRegistryDetails(type.Id, vessel.Name, "227441980", null, vessel.FlagState, null, null, null, null, Now.UtcDateTime);
        database.Add(vessel);
        await database.SaveChangesAsync();
        return vessel;
    }

    private static HarborOpsDbContext Database(string name) => new(new DbContextOptionsBuilder<HarborOpsDbContext>().UseInMemoryDatabase(name).Options);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
