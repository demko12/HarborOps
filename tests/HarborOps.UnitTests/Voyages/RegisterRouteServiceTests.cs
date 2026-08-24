using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RegisterRouteServiceTests
{
    [Fact]
    public async Task Register_persists_the_route_with_its_ordered_stops()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var genoaId = await SeedPortAsync(dbContext, "Port of Genoa");
        var service = new RegisterRouteService(dbContext);
        var request = new RegisterRouteRequest
        {
            Name = "  Adriatic feeder  ",
            Description = "  Bar to Genoa via Kotor  ",
            EstimatedDistance = 560m,
            EstimatedDuration = 2100,
            Stops =
            [
                CreateStop(genoaId, 3, 500m, 1800),
                CreateStop(barId, 1, 0m, 0),
                CreateStop(kotorId, 2, 60m, 300),
            ],
        };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var route = await dbContext.Set<Route>().SingleAsync(CancellationToken.None);
        var stops = await dbContext.Set<RoutePort>()
            .OrderBy(routePort => routePort.StopOrder)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(response.Id, route.Id);
        Assert.Equal("Adriatic feeder", route.Name);
        Assert.Equal("Bar to Genoa via Kotor", route.Description);
        Assert.Equal(560m, route.EstimatedDistance);
        Assert.Equal(2100, route.EstimatedDuration);
        Assert.Equal(3, stops.Count);
        Assert.All(stops, stop => Assert.Equal(route.Id, stop.RouteId));
        Assert.Equal([barId, kotorId, genoaId], stops.Select(stop => stop.PortId));
        Assert.Equal([1, 2, 3], stops.Select(stop => stop.StopOrder));
        Assert.Equal([0m, 60m, 500m], stops.Select(stop => stop.DistanceFromPrevious));
        Assert.Equal([0, 300, 1800], stops.Select(stop => stop.EstimatedDurationFromPrevious));
    }

    [Fact]
    public async Task Register_requires_at_least_two_stops()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, barId) with
        {
            Stops = [CreateStop(barId, 1, 0m, 0)],
        };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterRouteValidator.StopsField, exception.Errors.Keys, StringComparer.Ordinal);
        Assert.Empty(await dbContext.Set<Route>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Register_rejects_duplicate_stop_orders()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, kotorId) with
        {
            Stops = [CreateStop(barId, 1, 0m, 0), CreateStop(kotorId, 1, 60m, 300)],
        };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            "Route stop orders must be unique.",
            exception.Errors[RegisterRouteValidator.StopsField],
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_the_same_port_twice()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, barId) with
        {
            Stops = [CreateStop(barId, 1, 0m, 0), CreateStop(barId, 2, 60m, 300)],
        };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            "A port must not appear more than once in a route.",
            exception.Errors[RegisterRouteValidator.StopsField],
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_a_negative_stop_distance()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, kotorId) with
        {
            Stops = [CreateStop(barId, 1, 0m, 0), CreateStop(kotorId, 2, -60m, 300)],
        };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            "Route stop distances must not be negative.",
            exception.Errors[RegisterRouteValidator.StopsField],
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_requires_the_first_stop_to_start_from_zero()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, kotorId) with
        {
            EstimatedDistance = 70m,
            EstimatedDuration = 310,
            Stops = [CreateStop(barId, 1, 10m, 10), CreateStop(kotorId, 2, 60m, 300)],
        };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            "The first route stop must have no distance or duration from a previous port.",
            exception.Errors[RegisterRouteValidator.StopsField],
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_requires_every_following_stop_to_advance_the_route()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, kotorId) with
        {
            EstimatedDistance = 60m,
            EstimatedDuration = 0,
            Stops = [CreateStop(barId, 1, 0m, 0), CreateStop(kotorId, 2, 60m, 0)],
        };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            "Every route stop after the first must have a positive distance and duration from "
            + "the previous port.",
            exception.Errors[RegisterRouteValidator.StopsField],
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_an_estimated_distance_that_does_not_match_the_stops()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, kotorId) with { EstimatedDistance = 99m };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            RegisterRouteValidator.EstimatedDistanceField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_an_estimated_duration_that_does_not_match_the_stops()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterRouteService(dbContext);
        var request = CreateValidRequest(barId, kotorId) with { EstimatedDuration = 99 };

        var exception = await Assert.ThrowsAsync<RegisterRouteValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(
            RegisterRouteValidator.EstimatedDurationField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_a_stop_whose_port_does_not_exist()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var missingPortId = Guid.NewGuid();
        var service = new RegisterRouteService(dbContext);

        var exception = await Assert.ThrowsAsync<RouteStopPortNotFoundException>(
            () => service.RegisterAsync(
                CreateValidRequest(barId, missingPortId),
                CancellationToken.None));

        Assert.Equal(missingPortId, exception.PortId);
        Assert.Empty(await dbContext.Set<Route>().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.Set<RoutePort>().ToListAsync(CancellationToken.None));
    }

    private static RegisterRouteRequest CreateValidRequest(Guid firstPortId, Guid secondPortId)
    {
        return new RegisterRouteRequest
        {
            Name = "Adriatic feeder",
            Description = null,
            EstimatedDistance = 60m,
            EstimatedDuration = 300,
            Stops =
            [
                CreateStop(firstPortId, 1, 0m, 0),
                CreateStop(secondPortId, 2, 60m, 300),
            ],
        };
    }

    private static RegisterRouteStop CreateStop(
        Guid portId,
        int stopOrder,
        decimal distanceFromPrevious,
        int estimatedDurationFromPrevious)
    {
        return new RegisterRouteStop
        {
            PortId = portId,
            StopOrder = stopOrder,
            DistanceFromPrevious = distanceFromPrevious,
            EstimatedDurationFromPrevious = estimatedDurationFromPrevious,
        };
    }

    private static async Task<Guid> SeedPortAsync(HarborOpsDbContext dbContext, string name)
    {
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return port.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
