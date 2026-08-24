using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetRouteDetailsQueryTests
{
    [Fact]
    public async Task Details_project_the_stops_in_route_order_with_their_port_data()
    {
        await using var dbContext = CreateDbContext();
        var bar = Port.Create("Port of Bar", "Montenegro", "Bar", "MEBAR");
        var kotor = Port.Create("Port of Kotor", "Montenegro", "Kotor", "MEKOT");
        var genoa = Port.Create("Genoa terminal", "Italy", "Genoa", null);
        var route = Route.Create("Adriatic feeder", 560m, 2100, "Bar to Genoa via Kotor");

        await dbContext.Set<Port>().AddRangeAsync([bar, kotor, genoa], CancellationToken.None);
        await dbContext.Set<Route>().AddAsync(route, CancellationToken.None);
        await dbContext.Set<RoutePort>().AddRangeAsync(
            [
                RoutePort.Create(route.Id, genoa.Id, 3, 500m, 1800),
                RoutePort.Create(route.Id, bar.Id, 1, 0m, 0),
                RoutePort.Create(route.Id, kotor.Id, 2, 60m, 300),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var query = new GetRouteDetailsQuery(dbContext);

        var response = await query.GetRouteDetailsAsync(route.Id, CancellationToken.None);

        Assert.Equal(route.Id, response.RouteId);
        Assert.Equal("Adriatic feeder", response.Name);
        Assert.Equal("Bar to Genoa via Kotor", response.Description);
        Assert.Equal(560m, response.EstimatedDistance);
        Assert.Equal(2100, response.EstimatedDuration);
        Assert.Equal([1, 2, 3], response.Stops.Select(stop => stop.StopOrder));
        Assert.Equal(
            ["Port of Bar", "Port of Kotor", "Genoa terminal"],
            response.Stops.Select(stop => stop.PortName));
        Assert.Equal([0m, 60m, 500m], response.Stops.Select(stop => stop.DistanceFromPrevious));
        Assert.Equal(
            [0, 300, 1800],
            response.Stops.Select(stop => stop.EstimatedDurationFromPrevious));

        var firstStop = response.Stops[0];
        Assert.Equal(bar.Id, firstStop.PortId);
        Assert.Equal("Montenegro", firstStop.PortCountry);
        Assert.Equal("MEBAR", firstStop.UnLocode);
        Assert.Null(response.Stops[2].UnLocode);
    }

    [Fact]
    public async Task Details_reject_an_unknown_route()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetRouteDetailsQuery(dbContext);
        var missingRouteId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<RouteNotFoundException>(
            () => query.GetRouteDetailsAsync(missingRouteId, CancellationToken.None));

        Assert.Equal(missingRouteId, exception.RouteId);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
