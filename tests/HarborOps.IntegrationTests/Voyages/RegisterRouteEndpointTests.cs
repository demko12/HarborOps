using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class RegisterRouteEndpointTests : IntegrationTestBase
{
    private const string Path = "/api/voyages/routes";

    private readonly HarborOpsApiFactory _application;

    public RegisterRouteEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_persists_the_route_and_its_stops_together()
    {
        var barId = await SeedPortAsync("Port of Bar");
        var kotorId = await SeedPortAsync("Port of Kotor");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(barId, kotorId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterRouteResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"/api/voyages/routes/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var route = await dbContext.Set<Route>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);
        var stops = await dbContext.Set<RoutePort>()
            .Where(routePort => routePort.RouteId == result.Id)
            .OrderBy(routePort => routePort.StopOrder)
            .ToListAsync(CancellationToken.None);

        Assert.Equal("Adriatic feeder", route.Name);
        Assert.Equal(60m, route.EstimatedDistance);
        Assert.Equal(300, route.EstimatedDuration);
        Assert.Equal([barId, kotorId], stops.Select(stop => stop.PortId));
        Assert.Equal([1, 2], stops.Select(stop => stop.StopOrder));
    }

    [Fact]
    public async Task Post_with_a_single_stop_returns_400()
    {
        var barId = await SeedPortAsync("Port of Bar");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var request = CreateValidRequest(barId, barId) with
        {
            Stops = [CreateStop(barId, 1, 0m, 0)],
        };

        using var response = await client.PostAsJsonAsync(Path, request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("stops", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_with_totals_that_do_not_match_the_stops_returns_400()
    {
        var barId = await SeedPortAsync("Port of Bar");
        var kotorId = await SeedPortAsync("Port of Kotor");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var request = CreateValidRequest(barId, kotorId) with { EstimatedDistance = 999m };

        using var response = await client.PostAsJsonAsync(Path, request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("estimatedDistance", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_with_an_unknown_stop_port_returns_404()
    {
        var barId = await SeedPortAsync("Port of Bar");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(barId, Guid.NewGuid()),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("stops", problem.Errors.Keys, StringComparer.Ordinal);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        Assert.Empty(await dbContext.Set<Route>().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.Set<RoutePort>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.PostAsJsonAsync(
            Path,
            CreateValidRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static RegisterRouteRequest CreateValidRequest(Guid firstPortId, Guid secondPortId)
    {
        return new RegisterRouteRequest
        {
            Name = "Adriatic feeder",
            Description = "Bar to Kotor",
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

    private async Task<Guid> SeedPortAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return port.Id;
    }
}
