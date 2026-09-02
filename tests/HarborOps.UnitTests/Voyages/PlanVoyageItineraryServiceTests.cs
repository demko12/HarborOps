using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class PlanVoyageItineraryServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Plan_creates_ordered_stops_and_auto_generated_legs_for_explicit_ports()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-500");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new PlanVoyageItineraryService(dbContext);

        var request = new PlanVoyageItineraryRequest
        {
            Stops =
            [
                new PlanVoyageItineraryStop
                {
                    StopOrder = 1,
                    PortId = bar.Id,
                    DistanceFromPrevious = 0,
                    EstimatedDurationFromPrevious = 0,
                    Eta = PlannedDepartureUtc,
                    Etd = PlannedDepartureUtc.AddHours(4),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 2,
                    PortId = kotor.Id,
                    DistanceFromPrevious = 60m,
                    EstimatedDurationFromPrevious = 300,
                    Eta = PlannedDepartureUtc.AddHours(10),
                    Etd = PlannedDepartureUtc.AddHours(14),
                },
            ],
        };

        var response = await service.PlanAsync(voyage.Id, request, CancellationToken.None);

        Assert.Equal(2, response.Stops.Count);
        Assert.Equal([1, 2], response.Stops.Select(stop => stop.StopOrder));

        var persistedStops = await dbContext.Set<VoyageStop>()
            .Where(stop => stop.VoyageId == voyage.Id)
            .OrderBy(stop => stop.StopOrder)
            .ToListAsync(CancellationToken.None);

        Assert.All(persistedStops, stop => Assert.Equal(VoyageStop.PlannedStatus, stop.Status));

        var legs = await dbContext.Set<VoyageLeg>()
            .Where(leg => leg.VoyageId == voyage.Id)
            .ToListAsync(CancellationToken.None);

        var leg = Assert.Single(legs);
        Assert.Equal(persistedStops[0].Id, leg.FromStopId);
        Assert.Equal(persistedStops[1].Id, leg.ToStopId);
        Assert.Equal(60m, leg.Distance);
        Assert.Equal(300, leg.EstimatedDuration);
    }

    [Fact]
    public async Task Plan_derives_route_covered_stops_and_extends_with_explicit_stops()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-501");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var genoa = await SeedPortAsync(dbContext, "Genoa terminal");
        var extra = await SeedPortAsync(dbContext, "Extra port");

        var route = Route.Create("Adriatic feeder", 560m, 2100, null);
        await dbContext.Set<Route>().AddAsync(route, CancellationToken.None);
        await dbContext.Set<RoutePort>().AddRangeAsync(
            [
                RoutePort.Create(route.Id, bar.Id, 1, 0m, 0),
                RoutePort.Create(route.Id, kotor.Id, 2, 60m, 300),
                RoutePort.Create(route.Id, genoa.Id, 3, 500m, 1800),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new PlanVoyageItineraryService(dbContext);

        var request = new PlanVoyageItineraryRequest
        {
            RouteId = route.Id,
            Stops =
            [
                new PlanVoyageItineraryStop
                {
                    StopOrder = 1,
                    Eta = PlannedDepartureUtc,
                    Etd = PlannedDepartureUtc.AddHours(2),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 2,
                    Eta = PlannedDepartureUtc.AddHours(6),
                    Etd = PlannedDepartureUtc.AddHours(8),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 3,
                    Eta = PlannedDepartureUtc.AddHours(30),
                    Etd = PlannedDepartureUtc.AddHours(34),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 4,
                    PortId = extra.Id,
                    DistanceFromPrevious = 120m,
                    EstimatedDurationFromPrevious = 600,
                    Eta = PlannedDepartureUtc.AddHours(40),
                    Etd = PlannedDepartureUtc.AddHours(44),
                },
            ],
        };

        var response = await service.PlanAsync(voyage.Id, request, CancellationToken.None);

        Assert.Equal(4, response.Stops.Count);
        Assert.Equal(
            [bar.Id, kotor.Id, genoa.Id, extra.Id],
            response.Stops.Select(stop => stop.PortId));

        var legs = await dbContext.Set<VoyageLeg>()
            .Where(leg => leg.VoyageId == voyage.Id)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(3, legs.Count);
        Assert.Equal(120m, legs[2].Distance);
        Assert.Equal(600, legs[2].EstimatedDuration);
    }

    [Fact]
    public async Task Plan_rejects_non_increasing_chronology()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-502");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new PlanVoyageItineraryService(dbContext);

        var request = new PlanVoyageItineraryRequest
        {
            Stops =
            [
                new PlanVoyageItineraryStop
                {
                    StopOrder = 1,
                    PortId = bar.Id,
                    DistanceFromPrevious = 0,
                    EstimatedDurationFromPrevious = 0,
                    Eta = PlannedDepartureUtc,
                    Etd = PlannedDepartureUtc.AddHours(4),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 2,
                    PortId = kotor.Id,
                    DistanceFromPrevious = 60m,
                    EstimatedDurationFromPrevious = 300,
                    Eta = PlannedDepartureUtc.AddHours(3),
                    Etd = PlannedDepartureUtc.AddHours(6),
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<PlanVoyageItineraryValidationException>(
            () => service.PlanAsync(voyage.Id, request, CancellationToken.None));

        Assert.Contains(PlanVoyageItineraryValidator.StopsField, exception.Errors);
        Assert.False(await dbContext.Set<VoyageStop>().AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Plan_rejects_gaps_in_stop_order()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-503");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new PlanVoyageItineraryService(dbContext);

        var request = new PlanVoyageItineraryRequest
        {
            Stops =
            [
                new PlanVoyageItineraryStop
                {
                    StopOrder = 1,
                    PortId = bar.Id,
                    DistanceFromPrevious = 0,
                    EstimatedDurationFromPrevious = 0,
                    Eta = PlannedDepartureUtc,
                    Etd = PlannedDepartureUtc.AddHours(4),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 3,
                    PortId = kotor.Id,
                    DistanceFromPrevious = 60m,
                    EstimatedDurationFromPrevious = 300,
                    Eta = PlannedDepartureUtc.AddHours(10),
                    Etd = PlannedDepartureUtc.AddHours(14),
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<PlanVoyageItineraryValidationException>(
            () => service.PlanAsync(voyage.Id, request, CancellationToken.None));

        Assert.Contains(PlanVoyageItineraryValidator.StopsField, exception.Errors);
    }

    [Fact]
    public async Task Plan_rejects_voyage_that_is_not_planned()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-504");
        dbContext.Entry(voyage).Property(nameof(Voyage.Status)).CurrentValue =
            Voyage.InProgressStatus;
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new PlanVoyageItineraryService(dbContext);

        var request = ValidExplicitRequest(bar.Id, kotor.Id);

        var exception = await Assert.ThrowsAsync<VoyageItineraryNotPlannableException>(
            () => service.PlanAsync(voyage.Id, request, CancellationToken.None));

        Assert.Equal(Voyage.InProgressStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Plan_rejects_a_second_itinerary_for_the_same_voyage()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-505");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new PlanVoyageItineraryService(dbContext);
        var request = ValidExplicitRequest(bar.Id, kotor.Id);

        await service.PlanAsync(voyage.Id, request, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageItineraryAlreadyPlannedException>(
            () => service.PlanAsync(voyage.Id, request, CancellationToken.None));

        Assert.Equal(voyage.Id, exception.VoyageId);
    }

    private PlanVoyageItineraryRequest ValidExplicitRequest(Guid firstPortId, Guid secondPortId)
    {
        return new PlanVoyageItineraryRequest
        {
            Stops =
            [
                new PlanVoyageItineraryStop
                {
                    StopOrder = 1,
                    PortId = firstPortId,
                    DistanceFromPrevious = 0,
                    EstimatedDurationFromPrevious = 0,
                    Eta = PlannedDepartureUtc,
                    Etd = PlannedDepartureUtc.AddHours(4),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 2,
                    PortId = secondPortId,
                    DistanceFromPrevious = 60m,
                    EstimatedDurationFromPrevious = 300,
                    Eta = PlannedDepartureUtc.AddHours(10),
                    Etd = PlannedDepartureUtc.AddHours(14),
                },
            ],
        };
    }

    private static async Task<Port> SeedPortAsync(HarborOpsDbContext dbContext, string name)
    {
        var port = Port.Create(name, "Montenegro");
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.SaveChangesAsync();

        return port;
    }

    private static async Task<Voyage> SeedPlannedVoyageAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber)
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
