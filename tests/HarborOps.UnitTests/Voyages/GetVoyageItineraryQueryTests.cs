using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetVoyageItineraryQueryTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Itinerary_projects_stops_and_delays_in_deterministic_order()
    {
        await using var dbContext = CreateDbContext();
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            "VOY-1000",
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);
        var bar = Port.Create("Port of Bar", "Montenegro", "Bar", "MEBAR");
        var kotor = Port.Create("Port of Kotor", "Montenegro", "Kotor", "MEKOT");
        var berth = Berth.Create(bar.Id, "Pier 1", 200m, 10m, BerthStatus.Available);

        var stopTwo = VoyageStop.Create(
            voyage.Id,
            kotor.Id,
            2,
            PlannedDepartureUtc.AddHours(10),
            PlannedDepartureUtc.AddHours(14));
        var stopOne = VoyageStop.Create(
            voyage.Id,
            bar.Id,
            1,
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddHours(4));
        stopOne.AssignBerth(berth.Id);

        var delayOne = Delay.Create(
            voyage.Id,
            "Weather",
            60,
            PlannedDepartureUtc.AddHours(1),
            stopOne.Id,
            "Rough seas");
        var delayTwo = Delay.Create(
            voyage.Id,
            "Customs hold",
            30,
            PlannedDepartureUtc.AddMinutes(30));

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddRangeAsync([bar, kotor]);
        await dbContext.Set<Berth>().AddAsync(berth);
        await dbContext.Set<VoyageStop>().AddRangeAsync([stopTwo, stopOne]);
        await dbContext.Set<Delay>().AddRangeAsync([delayOne, delayTwo]);
        await dbContext.SaveChangesAsync();

        var query = new GetVoyageItineraryQuery(dbContext, TimeProvider.System);

        var response = await query.GetItineraryAsync(voyage.Id, CancellationToken.None);

        Assert.Equal(voyage.Id, response.VoyageId);
        Assert.Equal([1, 2], response.Stops.Select(stop => stop.StopOrder));
        Assert.Equal(bar.Id, response.Stops[0].PortId);
        Assert.Equal("Port of Bar", response.Stops[0].PortName);
        Assert.Equal("MEBAR", response.Stops[0].UnLocode);
        Assert.Equal(berth.Id, response.Stops[0].BerthId);
        Assert.Equal("Pier 1", response.Stops[0].BerthName);
        Assert.Equal(VoyageStop.PlannedStatus, response.Stops[0].Status);
        Assert.Null(response.Stops[1].BerthId);
        Assert.Null(response.Stops[1].BerthName);

        Assert.Equal(
            [delayTwo.Id, delayOne.Id],
            response.Delays.Select(delay => delay.DelayId));
        Assert.Equal(stopOne.Id, response.Delays[1].VoyageStopId);
        Assert.Null(response.Delays[0].VoyageStopId);
    }

    [Fact]
    public async Task Itinerary_carries_the_clearance_summary_and_overdue_count_per_stop()
    {
        await using var dbContext = PortCallClearanceScenario.CreateDbContext();
        var utcNow = PortCallClearanceScenario.UtcNow;
        var (voyageId, stopId) = await PortCallClearanceScenario
            .SeedArrivedStopWithClearanceAsync(dbContext);
        var clearance = await PortCallClearanceScenario.GetClearanceAsync(dbContext, stopId);

        await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Customs,
            "Overdue required",
            true,
            utcNow.AddHours(-2));
        var resolved = await PortCallClearanceScenario.SeedItemAsync(
            dbContext,
            stopId,
            PortCallClearanceCategory.Health,
            "Free pratique",
            true,
            utcNow.AddHours(-4));

        clearance.ClearItem(resolved, null, null, Guid.NewGuid(), utcNow);
        await dbContext.SaveChangesAsync();

        var query = new GetVoyageItineraryQuery(
            dbContext,
            new PortCallClearanceScenario.FixedTimeProvider(new DateTimeOffset(utcNow)));

        var response = await query.GetItineraryAsync(voyageId, CancellationToken.None);

        var stop = Assert.Single(response.Stops);
        Assert.Equal(PortCallClearance.PendingStatus, stop.ClearanceStatus);
        Assert.Equal(2, stop.ClearanceRequiredItemCount);
        Assert.Equal(1, stop.ClearanceRequiredResolvedCount);
        Assert.Equal(1, stop.ClearanceOverdueRequiredItemCount);
    }

    [Fact]
    public async Task Itinerary_carries_the_active_activity_count_and_statement_status_per_stop()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext, departed: true);
        var arrival = PortCallLogScenario.ActualArrivalUtc;

        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(arrival.AddMinutes(10)));
        await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.CargoStarted, PortCallLogScenario.At(arrival.AddHours(1)));
        var duplicate = await PortCallLogScenario.RecordAsync(dbContext, voyageId, stopId, PortCallActivityType.OperationalNote, PortCallLogScenario.At(arrival.AddHours(1)));
        await new VoidPortCallActivityService(dbContext, PortCallLogScenario.Clock()).VoidAsync(
            voyageId,
            stopId,
            duplicate.ActivityId,
            new Modules.Voyages.Contracts.VoidPortCallActivityRequest { Reason = "Duplicate", Version = duplicate.Version },
            PortCallLogScenario.ActorId,
            PortCallLogScenario.ActorName,
            CancellationToken.None);
        await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);

        var response = await new GetVoyageItineraryQuery(dbContext, PortCallLogScenario.Clock())
            .GetItineraryAsync(voyageId, CancellationToken.None);

        var stop = Assert.Single(response.Stops);
        Assert.Equal(2, stop.PortCallActivityCount);
        Assert.Equal(PortCallStatement.DraftStatus, stop.PortCallStatementStatus);
    }

    [Fact]
    public async Task Itinerary_reports_no_activity_log_for_a_stop_without_one()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, _) = await PortCallLogScenario.SeedStopAsync(dbContext, arrived: false);

        var response = await new GetVoyageItineraryQuery(dbContext, PortCallLogScenario.Clock())
            .GetItineraryAsync(voyageId, CancellationToken.None);

        var stop = Assert.Single(response.Stops);
        Assert.Equal(0, stop.PortCallActivityCount);
        Assert.Null(stop.PortCallStatementStatus);
    }

    [Fact]
    public async Task Itinerary_rejects_unknown_voyage()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetVoyageItineraryQuery(dbContext, TimeProvider.System);
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => query.GetItineraryAsync(unknownVoyageId, CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    [Fact]
    public async Task Itinerary_carries_the_vessel_the_voyage_is_scheduled_for()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var voyage = Voyage.Schedule(
            vesselId,
            "VOY-1400",
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        var query = new GetVoyageItineraryQuery(dbContext, TimeProvider.System);

        var response = await query.GetItineraryAsync(voyage.Id, CancellationToken.None);

        Assert.Equal(vesselId, response.VesselId);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
